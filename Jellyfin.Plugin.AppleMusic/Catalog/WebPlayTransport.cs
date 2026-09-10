using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Talks to Apple's internal catalog endpoint using the web player token.
/// </summary>
/// <remarks>
/// The endpoint rejects requests without an <c>Origin</c> header, because the
/// token restricts itself to <c>apple.com</c> origins via its
/// <c>root_https_origin</c> claim.
/// </remarks>
public class WebPlayTransport : ICatalogTransport
{
    private const string BaseUrl = "https://amp-api.music.apple.com";
    private const string Origin = "https://music.apple.com";

    private readonly HttpClient _httpClient;
    private readonly IWebPlayTokenProvider _tokenProvider;
    private readonly ILogger<WebPlayTransport> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPlayTransport"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="tokenProvider">Web player token provider.</param>
    /// <param name="logger">Logger.</param>
    public WebPlayTransport(
        HttpClient httpClient,
        IWebPlayTokenProvider tokenProvider,
        ILogger<WebPlayTransport> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relativeUrl);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("Origin", Origin);

        _logger.LogDebug("GET {Url}", relativeUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected while walking storefronts: the resource simply is not
            // available in this one.
            _logger.LogDebug("Not found: {Url}", relativeUrl);
            return null;
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // amp-api does not report remaining quota, so there is nothing to
            // back off against — give up on this lookup rather than retry.
            _logger.LogWarning("Apple Music rate limited the request to {Url}", relativeUrl);
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
