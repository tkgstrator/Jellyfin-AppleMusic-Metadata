using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public sealed class CatalogCacheTests : IDisposable
{
    // Measured against the live API: a 25-result search comes back at ~31 KB,
    // an id lookup at ~1.5 KB.
    private const int SearchResponseBytes = 31 * 1024;
    private const int IdResponseBytes = 1536;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "apple-music-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task SetThenGet_ReturnsTheStoredBody()
    {
        var cache = NewCache();

        await cache.SetAsync("/v1/catalog/jp/songs/1", "{\"data\":[]}", TestContext.Current.CancellationToken);
        var hit = await cache.GetAsync("/v1/catalog/jp/songs/1", TestContext.Current.CancellationToken);

        Assert.NotNull(hit);
        Assert.Equal("{\"data\":[]}", hit.Body);
    }

    [Fact]
    public async Task Get_MissesOnAnUnknownKey()
    {
        var cache = NewCache();

        Assert.Null(await cache.GetAsync("/nothing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Set_RemembersAbsenceAsAMeaningfulAnswer()
    {
        var cache = NewCache();

        await cache.SetAsync("/v1/catalog/jp/songs/404", null, TestContext.Current.CancellationToken);
        var hit = await cache.GetAsync("/v1/catalog/jp/songs/404", TestContext.Current.CancellationToken);

        Assert.NotNull(hit);   // a hit...
        Assert.Null(hit.Body); // ...whose answer is "not there"
    }

    [Fact]
    public async Task Get_TreatsExpiredEntriesAsMisses()
    {
        var cache = NewCache(new CatalogCacheOptions { Lifetime = TimeSpan.FromMilliseconds(1) });

        await cache.SetAsync("/expiring", "value", TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Null(await cache.GetAsync("/expiring", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_ReturnsNothingWhenCachingIsDisabled()
    {
        var options = new CatalogCacheOptions();
        var cache = NewCache(options);
        await cache.SetAsync("/key", "value", TestContext.Current.CancellationToken);

        options.Enabled = false;

        Assert.Null(await cache.GetAsync("/key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Memory_StaysWithinItsBudgetAcrossALargeLibrary()
    {
        // 20,000 search responses at ~31 KB would be ~620 MB if everything were
        // retained. The budget must hold regardless.
        const long Budget = 8L * 1024 * 1024;
        var cache = NewCache(new CatalogCacheOptions
        {
            MaxMemoryBytes = Budget,
            MaxPersistedEntryBytes = 0, // keep this test off the disk
        });

        var body = Body(SearchResponseBytes);
        for (var i = 0; i < 20_000; i++)
        {
            await cache.SetAsync($"/v1/catalog/jp/search?term=track{i}", body, TestContext.Current.CancellationToken);
        }

        Assert.True(
            cache.MemoryBytes <= Budget,
            $"memory budget exceeded: {cache.MemoryBytes} > {Budget}");
    }

    [Fact]
    public async Task Memory_EvictsTheLeastRecentlyUsedEntry()
    {
        var cache = NewCache(new CatalogCacheOptions
        {
            MaxMemoryBytes = IdResponseBytes * 2,
            MaxPersistedEntryBytes = 0,
        });
        var body = Body(IdResponseBytes);

        await cache.SetAsync("/a", body, TestContext.Current.CancellationToken);
        await cache.SetAsync("/b", body, TestContext.Current.CancellationToken);

        // Touch /a so /b becomes the least recently used.
        await cache.GetAsync("/a", TestContext.Current.CancellationToken);
        await cache.SetAsync("/c", body, TestContext.Current.CancellationToken);

        Assert.NotNull(await cache.GetAsync("/a", TestContext.Current.CancellationToken));
        Assert.NotNull(await cache.GetAsync("/c", TestContext.Current.CancellationToken));
        Assert.Null(await cache.GetAsync("/b", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disk_KeepsSmallEntriesButNotLargeOnes()
    {
        var options = new CatalogCacheOptions { MaxPersistedEntryBytes = 8 * 1024 };

        var first = NewCache(options);
        await first.SetAsync("/small", Body(IdResponseBytes), TestContext.Current.CancellationToken);
        await first.SetAsync("/large", Body(SearchResponseBytes), TestContext.Current.CancellationToken);

        // A fresh instance shares only the disk tier, never the memory tier.
        var second = NewCache(options);

        Assert.NotNull(await second.GetAsync("/small", TestContext.Current.CancellationToken));
        Assert.Null(await second.GetAsync("/large", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disk_DoesNotServeExpiredEntries()
    {
        var options = new CatalogCacheOptions { Lifetime = TimeSpan.FromMilliseconds(1) };
        var first = NewCache(options);
        await first.SetAsync("/stale", Body(IdResponseBytes), TestContext.Current.CancellationToken);

        await Task.Delay(20, TestContext.Current.CancellationToken);
        var second = NewCache(options);

        Assert.Null(await second.GetAsync("/stale", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disk_ShardsFilesIntoSubdirectories()
    {
        var cache = NewCache();

        for (var i = 0; i < 200; i++)
        {
            await cache.SetAsync($"/key/{i}", Body(IdResponseBytes), TestContext.Current.CancellationToken);
        }

        var shards = Directory.GetDirectories(_root);
        Assert.True(shards.Length > 1, "entries should be spread across shard directories");
        Assert.Equal(200, Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Get_SurvivesACorruptFile()
    {
        var cache = NewCache();
        await cache.SetAsync("/key", Body(IdResponseBytes), TestContext.Current.CancellationToken);

        foreach (var file in Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(file, "this is not json", TestContext.Current.CancellationToken);
        }

        var fresh = NewCache();

        Assert.Null(await fresh.GetAsync("/key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prune_RemovesOnlyExpiredFiles()
    {
        var live = NewCache();
        await live.SetAsync("/live", Body(IdResponseBytes), TestContext.Current.CancellationToken);

        var expiring = NewCache(new CatalogCacheOptions { Lifetime = TimeSpan.FromMilliseconds(1) });
        await expiring.SetAsync("/dead", Body(IdResponseBytes), TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        var removed = await live.PruneAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Single(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Clear_EmptiesBothTiers()
    {
        var cache = NewCache();
        await cache.SetAsync("/key", Body(IdResponseBytes), TestContext.Current.CancellationToken);

        cache.Clear();

        Assert.Equal(0, cache.MemoryCount);
        Assert.Null(await cache.GetAsync("/key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Transport_ServesTheSecondLookupFromCache()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        var transport = NewTransport(inner);

        await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken);
        await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Transport_StillDistinguishesDifferentUrls()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        var transport = NewTransport(inner);

        await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken);
        await transport.GetAsync("/v1/catalog/us/albums/1", TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Transport_CollapsesConcurrentLookupsOfTheSameUrl()
    {
        // What a library scan does: every track of an album asks for the same
        // album at once, before anything has been cached.
        var inner = new CountingTransport("{\"data\":[]}", delay: TimeSpan.FromMilliseconds(50));
        var transport = NewTransport(inner);

        var lookups = new List<Task<string?>>();
        for (var i = 0; i < 20; i++)
        {
            lookups.Add(transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(lookups);

        Assert.Equal(1, inner.Calls);
        Assert.All(lookups, task => Assert.Equal("{\"data\":[]}", task.Result));
    }

    [Fact]
    public async Task Transport_CachesAbsenceSoItIsNotRefetched()
    {
        var inner = new CountingTransport(null);
        var transport = NewTransport(inner);

        Assert.Null(await transport.GetAsync("/missing", TestContext.Current.CancellationToken));
        Assert.Null(await transport.GetAsync("/missing", TestContext.Current.CancellationToken));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Transport_DoesNotRememberARateLimitedLookupAsAbsent()
    {
        var inner = new FlakyTransport(failures: 1, body: "{\"data\":[]}");
        var transport = NewTransport(inner);

        await Assert.ThrowsAsync<CatalogRateLimitedException>(
            () => transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken));

        // The second call must reach the network again instead of hitting a cached null.
        Assert.Equal("{\"data\":[]}", await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Transport_PropagatesRateLimitingToJoinedLookups()
    {
        var inner = new FlakyTransport(failures: 1, body: null, delay: TimeSpan.FromMilliseconds(100));
        var transport = NewTransport(inner);

        var lookups = Enumerable.Range(0, 4)
            .Select(_ => transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken))
            .ToArray();

        foreach (var lookup in lookups)
        {
            await Assert.ThrowsAsync<CatalogRateLimitedException>(() => lookup);
        }

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Transport_GoesStraightThroughWhenCachingIsDisabled()
    {
        var inner = new CountingTransport("{\"data\":[]}");
        var transport = NewTransport(inner, new CatalogCacheOptions { Enabled = false });

        await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken);
        await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Calls);
    }

    private static string Body(int bytes) => new('x', bytes);

    private CatalogCache NewCache(CatalogCacheOptions? options = null)
    {
        var resolved = options ?? new CatalogCacheOptions();
        return new CatalogCache(_root, () => resolved, NullLogger<CatalogCache>.Instance);
    }

    private CachingCatalogTransport NewTransport(ICatalogTransport inner, CatalogCacheOptions? options = null)
        => new(inner, NewCache(options), NullLogger<CachingCatalogTransport>.Instance);

    private sealed class FlakyTransport : ICatalogTransport
    {
        private readonly string? _body;
        private readonly TimeSpan _delay;
        private int _failures;
        private int _calls;

        public FlakyTransport(int failures, string? body, TimeSpan delay = default)
        {
            _failures = failures;
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

            if (Interlocked.Decrement(ref _failures) >= 0)
            {
                throw new CatalogRateLimitedException();
            }

            return _body;
        }
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
