using System;
using System.Buffers.Text;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Obtains the Apple Music web player bearer token by reading it out of the
/// script bundle that <c>music.apple.com</c> serves.
/// </summary>
/// <remarks>
/// The token is not tied to any developer account — it is the one Apple hands
/// to every visitor of the web player — and it carries a
/// <c>root_https_origin</c> claim, which is why requests must also send an
/// <c>Origin</c> header. It rotates roughly every 70 days, so it is cached
/// until shortly before expiry and then re-fetched.
/// </remarks>
public sealed partial class WebPlayTokenProvider : IWebPlayTokenProvider, IDisposable
{
    private const string HomePageUrl = "https://music.apple.com/";
    private const string AssetsBaseUrl = "https://music.apple.com";
    private const string WebPlayKeyId = "WebPlayKid";
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private static readonly TimeSpan _refreshMargin = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebPlayTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPlayTokenProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client used to fetch the page and bundle.</param>
    /// <param name="logger">Logger.</param>
    public WebPlayTokenProvider(HttpClient httpClient, ILogger<WebPlayTokenProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while we waited for the lock.
            if (TryGetCachedToken(out cached))
            {
                return cached;
            }

            return await RefreshAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _refreshLock.Dispose();
        _isDisposed = true;
    }

    /// <summary>
    /// Reads the <c>kid</c> claim from a JWT header.
    /// </summary>
    /// <param name="token">Encoded JWT.</param>
    /// <returns>The key identifier, or null when it cannot be read.</returns>
    internal static string? ReadKeyId(string token)
    {
        var header = DecodeSegment(token, 0);
        if (header is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(header);
            return document.RootElement.TryGetProperty("kid", out var kid) ? kid.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the <c>exp</c> claim from a JWT payload.
    /// </summary>
    /// <param name="token">Encoded JWT.</param>
    /// <returns>
    /// The expiry instant, or <see cref="DateTimeOffset.MinValue"/> when it
    /// cannot be read — treating the token as already stale is safer than
    /// trusting one with an unknown lifetime.
    /// </returns>
    internal static DateTimeOffset ReadExpiry(string token)
    {
        var payload = DecodeSegment(token, 1);
        if (payload is null)
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch (JsonException)
        {
            // Fall through to the stale default.
        }

        return DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Finds the web player token among the JWTs embedded in a script bundle.
    /// </summary>
    /// <param name="bundle">Script bundle contents.</param>
    /// <returns>The token, or null when the bundle carries none.</returns>
    internal static string? ExtractToken(string bundle)
    {
        foreach (var match in JwtRegex().EnumerateMatches(bundle))
        {
            var token = bundle.Substring(match.Index, match.Length);
            if (string.Equals(ReadKeyId(token), WebPlayKeyId, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the modern script bundle referenced by the web player page.
    /// </summary>
    /// <param name="html">Page markup.</param>
    /// <returns>Absolute bundle URL, or null when no reference was found.</returns>
    internal static string? ExtractBundleUrl(string html)
    {
        var match = BundleUrlRegex().Match(html);
        return match.Success ? AssetsBaseUrl + match.Value : null;
    }

    private static string? DecodeSegment(string token, int index)
    {
        var parts = token.Split('.');
        if (parts.Length <= index)
        {
            return null;
        }

        // JWTs use base64url, which substitutes '-' and '_' for '+' and '/',
        // and drops the padding.
        var value = parts[index].Replace('-', '+').Replace('_', '/');
        var padding = (4 - (value.Length % 4)) % 4;
        if (padding == 3)
        {
            // Not a valid base64 length; three padding characters never occur.
            return null;
        }

        value = value.PadRight(value.Length + padding, '=');

        Span<byte> buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer[..written]);
    }

    private bool TryGetCachedToken(out string token)
    {
        var cached = _token;
        if (cached is not null && DateTimeOffset.UtcNow < _expiresAt - _refreshMargin)
        {
            token = cached;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching the Apple Music web player token");

        var html = await GetStringAsync(HomePageUrl, cancellationToken);
        var bundleUrl = ExtractBundleUrl(html);
        if (bundleUrl is null)
        {
            _logger.LogError("No web player script bundle was referenced by {Url}", HomePageUrl);
            throw new InvalidOperationException(
                "Could not locate the Apple Music web player script bundle. The site layout may have changed.");
        }

        _logger.LogDebug("Reading the web player token from {BundleUrl}", bundleUrl);
        var bundle = await GetStringAsync(bundleUrl, cancellationToken);

        var token = ExtractToken(bundle);
        if (token is null)
        {
            _logger.LogError("No '{KeyId}' token was found in {BundleUrl}", WebPlayKeyId, bundleUrl);
            throw new InvalidOperationException(
                "Could not extract the Apple Music web player token. The site layout may have changed.");
        }

        _token = token;
        _expiresAt = ReadExpiry(token);
        _logger.LogInformation("Web player token acquired, expires at {ExpiresAt}", _expiresAt);
        return token;
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // Matches the modern bundle only: 'index-legacy~...' deliberately does not
    // match, since the two carry the same token and one is enough.
    [GeneratedRegex(@"/assets/index~[A-Za-z0-9]+\.js")]
    private static partial Regex BundleUrlRegex();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+")]
    private static partial Regex JwtRegex();
}
