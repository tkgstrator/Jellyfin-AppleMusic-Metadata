using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public sealed class CatalogCacheTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "apple-music-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsTheStoredBody()
    {
        using var cache = NewCache();

        cache.Set("/v1/catalog/jp/songs/1", "{\"data\":[]}");

        Assert.True(cache.TryGet("/v1/catalog/jp/songs/1", out var body));
        Assert.Equal("{\"data\":[]}", body);
    }

    [Fact]
    public void TryGet_MissesOnAnUnknownKey()
    {
        using var cache = NewCache();

        Assert.False(cache.TryGet("/nothing", out _));
    }

    [Fact]
    public void Set_RemembersAbsenceSeparately()
    {
        using var cache = NewCache();

        cache.Set("/v1/catalog/jp/songs/404", null);

        // A hit whose value is null: known to be absent, so no refetch.
        Assert.True(cache.TryGet("/v1/catalog/jp/songs/404", out var body));
        Assert.Null(body);
    }

    [Fact]
    public async Task TryGet_TreatsExpiredEntriesAsMisses()
    {
        using var cache = NewCache(new CatalogCacheOptions { Lifetime = TimeSpan.FromMilliseconds(1) });

        cache.Set("/expiring", "value");
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.False(cache.TryGet("/expiring", out _));
    }

    [Fact]
    public void TryGet_ReturnsNothingWhenCachingIsDisabled()
    {
        var options = new CatalogCacheOptions();
        using var cache = NewCache(options);
        cache.Set("/key", "value");

        options.Enabled = false;

        Assert.False(cache.TryGet("/key", out _));
    }

    [Fact]
    public void Set_TrimsDownToTheConfiguredLimit()
    {
        using var cache = NewCache(new CatalogCacheOptions { MaxEntries = 5 });

        for (var i = 0; i < 20; i++)
        {
            cache.Set($"/key/{i}", "value");
        }

        Assert.True(cache.Count <= 5);
    }

    [Fact]
    public async Task Entries_SurviveARestart()
    {
        var path = Path.Combine(_directory, "catalog.json");

        using (var first = NewCache(path: path))
        {
            first.Set("/v1/catalog/jp/albums/1440791809", "{\"cached\":true}");
            await first.FlushAsync(CancellationToken.None);
        }

        using var second = NewCache(path: path);

        Assert.True(second.TryGet("/v1/catalog/jp/albums/1440791809", out var body));
        Assert.Equal("{\"cached\":true}", body);
    }

    [Fact]
    public async Task ExpiredEntriesAreNotReloaded()
    {
        var path = Path.Combine(_directory, "catalog.json");

        using (var first = NewCache(new CatalogCacheOptions { Lifetime = TimeSpan.FromMilliseconds(1) }, path))
        {
            first.Set("/stale", "value");
            await first.FlushAsync(CancellationToken.None);
        }

        await Task.Delay(20, TestContext.Current.CancellationToken);
        using var second = NewCache(path: path);

        Assert.Equal(0, second.Count);
    }

    [Fact]
    public void Load_SurvivesACorruptFile()
    {
        var path = Path.Combine(_directory, "catalog.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "this is not json");

        using var cache = NewCache(path: path);

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("/anything", out _));
    }

    [Fact]
    public async Task Transport_ServesTheSecondLookupFromCache()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        using var cache = NewCache();
        var transport = new CachingCatalogTransport(inner, cache, NullLogger<CachingCatalogTransport>.Instance);

        await transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None);
        await transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Transport_StillDistinguishesDifferentUrls()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        using var cache = NewCache();
        var transport = new CachingCatalogTransport(inner, cache, NullLogger<CachingCatalogTransport>.Instance);

        await transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None);
        await transport.GetAsync("/v1/catalog/us/albums/1", CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Transport_CollapsesConcurrentLookupsOfTheSameUrl()
    {
        // What a library scan does: every track of an album asks for the same
        // album at once, before anything has been cached.
        var inner = new CountingTransport("{\"data\":[]}", delay: TimeSpan.FromMilliseconds(50));
        using var cache = NewCache();
        var transport = new CachingCatalogTransport(inner, cache, NullLogger<CachingCatalogTransport>.Instance);

        var lookups = new List<Task<string?>>();
        for (var i = 0; i < 20; i++)
        {
            lookups.Add(transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None));
        }

        await Task.WhenAll(lookups);

        Assert.Equal(1, inner.Calls);
        Assert.All(lookups, task => Assert.Equal("{\"data\":[]}", task.Result));
    }

    [Fact]
    public async Task Transport_CachesAbsenceSoItIsNotRefetched()
    {
        var inner = new CountingTransport(null);
        using var cache = NewCache();
        var transport = new CachingCatalogTransport(inner, cache, NullLogger<CachingCatalogTransport>.Instance);

        Assert.Null(await transport.GetAsync("/missing", CancellationToken.None));
        Assert.Null(await transport.GetAsync("/missing", CancellationToken.None));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Transport_GoesStraightThroughWhenCachingIsDisabled()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        using var cache = NewCache(new CatalogCacheOptions { Enabled = false });
        var transport = new CachingCatalogTransport(inner, cache, NullLogger<CachingCatalogTransport>.Instance);

        await transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None);
        await transport.GetAsync("/v1/catalog/jp/albums/1", CancellationToken.None);

        Assert.Equal(2, inner.Calls);
    }

    private FileCatalogCache NewCache(CatalogCacheOptions? options = null, string? path = null)
    {
        var resolved = options ?? new CatalogCacheOptions();
        return new FileCatalogCache(
            path ?? Path.Combine(_directory, "catalog.json"),
            () => resolved,
            NullLogger<FileCatalogCache>.Instance);
    }

    private sealed class CountingTransport : ICatalogTransport
    {
        private readonly string? _body;
        private readonly TimeSpan _delay;
        private int _calls;

        public CountingTransport(string? body, TimeSpan delay = default)
        {
            _body = body;
            _delay = delay;
        }

        public int Calls => _calls;

        public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return _body;
        }
    }
}
