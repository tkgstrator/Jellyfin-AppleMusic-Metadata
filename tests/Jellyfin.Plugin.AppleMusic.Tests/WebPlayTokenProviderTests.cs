using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class WebPlayTokenProviderTests
{
    // Both bundles are referenced by the real page; only the modern one should
    // be picked up.
    private const string PageHtml = """
        <html><body>
        <script type="module" src="/assets/index~3eb8a0d364.js"></script>
        <script nomodule src="/assets/index-legacy~a5e638ce59.js"></script>
        </body></html>
        """;

    [Fact]
    public void ExtractBundleUrl_PicksTheModernBundle()
    {
        var url = WebPlayTokenProvider.ExtractBundleUrl(PageHtml);

        Assert.Equal("https://music.apple.com/assets/index~3eb8a0d364.js", url);
    }

    [Fact]
    public void ExtractBundleUrl_ReturnsNullWhenAbsent()
    {
        Assert.Null(WebPlayTokenProvider.ExtractBundleUrl("<html><body>nothing here</body></html>"));
    }

    [Fact]
    public void ReadKeyId_ReadsTheHeaderClaim()
    {
        var token = MakeJwt("WebPlayKid", 1792680924);

        Assert.Equal("WebPlayKid", WebPlayTokenProvider.ReadKeyId(token));
    }

    [Fact]
    public void ReadExpiry_ReadsThePayloadClaim()
    {
        // 1792680924 == 2026-10-22T14:55:24Z, the expiry observed in the wild.
        var token = MakeJwt("WebPlayKid", 1792680924);

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1792680924),
            WebPlayTokenProvider.ReadExpiry(token));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9")]
    public void ReadExpiry_TreatsUnreadableTokensAsStale(string token)
    {
        Assert.Equal(DateTimeOffset.MinValue, WebPlayTokenProvider.ReadExpiry(token));
    }

    [Fact]
    public void ExtractToken_SelectsWebPlayKidAmongSeveralTokens()
    {
        var wanted = MakeJwt("WebPlayKid", 1792680924);
        var bundle = $"var a={MakeJwt("97DQU9QUD6", 1)};var b={MakeJwt("LT2ZDZSXNQ", 2)};var c={wanted};";

        Assert.Equal(wanted, WebPlayTokenProvider.ExtractToken(bundle));
    }

    [Fact]
    public void ExtractToken_ReturnsNullWhenNoWebPlayTokenIsPresent()
    {
        var bundle = $"var a={MakeJwt("97DQU9QUD6", 1)};";

        Assert.Null(WebPlayTokenProvider.ExtractToken(bundle));
    }

    [Fact]
    public async Task GetTokenAsync_FetchesPageThenBundle()
    {
        var expected = MakeJwt("WebPlayKid", FarFuture());
        var handler = new StubHandler(PageHtml, $"const token={expected};");
        using var client = new HttpClient(handler);
        using var provider = new WebPlayTokenProvider(client, NullLogger<WebPlayTokenProvider>.Instance);

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(expected, token);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal("https://music.apple.com/", handler.Requests[0]);
        Assert.Equal("https://music.apple.com/assets/index~3eb8a0d364.js", handler.Requests[1]);
    }

    [Fact]
    public async Task GetTokenAsync_CachesUntilCloseToExpiry()
    {
        var handler = new StubHandler(PageHtml, $"const token={MakeJwt("WebPlayKid", FarFuture())};");
        using var client = new HttpClient(handler);
        using var provider = new WebPlayTokenProvider(client, NullLogger<WebPlayTokenProvider>.Instance);

        var first = await provider.GetTokenAsync(CancellationToken.None);
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(2, handler.CallCount); // still just the initial page + bundle
    }

    [Fact]
    public async Task GetTokenAsync_RefetchesWhenTheTokenIsAboutToExpire()
    {
        // Inside the 24 hour refresh margin, so it must not be reused.
        var nearlyExpired = MakeJwt("WebPlayKid", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        var handler = new StubHandler(PageHtml, $"const token={nearlyExpired};");
        using var client = new HttpClient(handler);
        using var provider = new WebPlayTokenProvider(client, NullLogger<WebPlayTokenProvider>.Instance);

        await provider.GetTokenAsync(CancellationToken.None);
        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(4, handler.CallCount); // fetched twice
    }

    [Fact]
    public async Task GetTokenAsync_ThrowsWhenTheBundleReferenceIsGone()
    {
        var handler = new StubHandler("<html>no bundle</html>", string.Empty);
        using var client = new HttpClient(handler);
        using var provider = new WebPlayTokenProvider(client, NullLogger<WebPlayTokenProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetTokenAsync_ThrowsWhenTheBundleCarriesNoToken()
    {
        var handler = new StubHandler(PageHtml, "const nothing = 1;");
        using var client = new HttpClient(handler);
        using var provider = new WebPlayTokenProvider(client, NullLogger<WebPlayTokenProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(CancellationToken.None));
    }

    private static long FarFuture() => DateTimeOffset.UtcNow.AddDays(60).ToUnixTimeSeconds();

    private static string MakeJwt(string keyId, long expiry)
    {
        var header = Base64Url($$"""{"typ":"JWT","alg":"ES256","kid":"{{keyId}}"}""");
        var payload = Base64Url($$"""{"iss":"AMPWebPlay","exp":{{expiry}},"root_https_origin":["apple.com"]}""");
        return $"{header}.{payload}.{Base64Url("signature")}";
    }

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _page;
        private readonly string _bundle;

        public StubHandler(string page, string bundle)
        {
            _page = page;
            _bundle = bundle;
        }

        public int CallCount { get; private set; }

        public System.Collections.Generic.List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            var body = url.Contains("/assets/", StringComparison.Ordinal) ? _bundle : _page;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
