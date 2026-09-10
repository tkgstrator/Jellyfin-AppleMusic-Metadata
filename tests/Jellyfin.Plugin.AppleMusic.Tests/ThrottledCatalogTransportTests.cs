using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Throttling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class ThrottledCatalogTransportTests
{
    [Fact]
    public async Task GetAsync_SendsOneRequestAtATime()
    {
        var inner = new ScriptedTransport(delay: TimeSpan.FromMilliseconds(30));
        using var transport = Build(inner, new ThrottleOptions { MinInterval = TimeSpan.Zero });

        var lookups = Enumerable.Range(0, 5)
            .Select(i => transport.GetAsync($"/v1/{i}", TestContext.Current.CancellationToken));
        await Task.WhenAll(lookups);

        Assert.Equal(1, inner.MaxConcurrency);
        Assert.Equal(5, inner.Calls);
    }

    [Fact]
    public async Task GetAsync_SpacesRequestsByTheMinimumInterval()
    {
        var interval = TimeSpan.FromMilliseconds(80);
        var inner = new ScriptedTransport();
        using var transport = Build(inner, new ThrottleOptions { MinInterval = interval });

        await transport.GetAsync("/v1/a", TestContext.Current.CancellationToken);
        await transport.GetAsync("/v1/b", TestContext.Current.CancellationToken);
        await transport.GetAsync("/v1/c", TestContext.Current.CancellationToken);

        var gaps = inner.Starts.Zip(inner.Starts.Skip(1), (earlier, later) => later - earlier);
        Assert.All(gaps, gap => Assert.True(gap >= interval - TimeSpan.FromMilliseconds(15), $"gap {gap} shorter than {interval}"));
    }

    [Fact]
    public async Task GetAsync_RetriesAfterTheCooldownWhenRateLimited()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: 1);
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromMilliseconds(60),
            MaxAttempts = 3,
        });

        var body = await transport.GetAsync("/v1/a", TestContext.Current.CancellationToken);

        Assert.Equal("ok", body);
        Assert.Equal(2, inner.Calls);
        Assert.True(inner.Starts[1] - inner.Starts[0] >= TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public async Task GetAsync_GivesUpAfterTheConfiguredAttempts()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: int.MaxValue);
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromMilliseconds(10),
            MaxCooldown = TimeSpan.FromSeconds(10),
            MaxAttempts = 3,
        });

        await Assert.ThrowsAsync<CatalogRateLimitedException>(
            () => transport.GetAsync("/v1/a", TestContext.Current.CancellationToken));

        Assert.Equal(3, inner.Calls);
        Assert.True(transport.IsCoolingDown);
    }

    [Fact]
    public async Task GetAsync_PausesOtherLookupsDuringTheCooldown()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: 1);
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromMilliseconds(80),
            MaxAttempts = 2,
        });

        var first = transport.GetAsync("/v1/a", TestContext.Current.CancellationToken);
        await inner.FirstCallStarted.Task;
        var second = transport.GetAsync("/v1/b", TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second);

        // The retry of /v1/a and the fresh /v1/b both had to wait out the pause.
        Assert.Equal(3, inner.Calls);
        Assert.All(inner.Starts.Skip(1), start => Assert.True(start - inner.Starts[0] >= TimeSpan.FromMilliseconds(65)));
    }

    [Fact]
    public async Task GetAsync_FailsFastWhileTheCatalogKeepsRefusing()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: int.MaxValue);
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromSeconds(10),
            MaxCooldown = TimeSpan.FromSeconds(10),
            MaxAttempts = 1,
        });

        await Assert.ThrowsAsync<CatalogRateLimitedException>(
            () => transport.GetAsync("/v1/a", TestContext.Current.CancellationToken));

        // The cooldown is at its ceiling, so this must not wait ten seconds.
        var started = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<CatalogRateLimitedException>(
            () => transport.GetAsync("/v1/b", TestContext.Current.CancellationToken));

        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task GetAsync_ResetsTheCooldownAfterASuccess()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: 1);
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromMilliseconds(20),
            MaxAttempts = 2,
        });

        await transport.GetAsync("/v1/a", TestContext.Current.CancellationToken);

        Assert.False(transport.IsCoolingDown);
    }

    [Fact]
    public async Task GetAsync_DoesNotLetASearchCooldownBlockIdLookups()
    {
        var inner = new ScriptedTransport(rateLimitedCalls: int.MaxValue, refuseOnly: "/search?");
        using var transport = Build(inner, new ThrottleOptions
        {
            MinInterval = TimeSpan.Zero,
            InitialCooldown = TimeSpan.FromSeconds(10),
            MaxCooldown = TimeSpan.FromSeconds(10),
            MaxAttempts = 1,
        });

        await Assert.ThrowsAsync<CatalogRateLimitedException>(
            () => transport.GetAsync("/v1/catalog/jp/search?term=x", TestContext.Current.CancellationToken));

        var started = DateTimeOffset.UtcNow;
        Assert.Equal("ok", await transport.GetAsync("/v1/catalog/jp/albums/1", TestContext.Current.CancellationToken));
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.True(transport.IsCoolingDown); // the search side is still paused
    }

    private static ThrottledCatalogTransport Build(ICatalogTransport inner, ThrottleOptions options)
        => new(inner, () => options, NullLogger<ThrottledCatalogTransport>.Instance);

    private sealed class ScriptedTransport : ICatalogTransport
    {
        private readonly TimeSpan _delay;
        private readonly object _lock = new();
        private int _rateLimitedCalls;
        private int _inFlight;

        private readonly string? _refuseOnly;

        public ScriptedTransport(TimeSpan delay = default, int rateLimitedCalls = 0, string? refuseOnly = null)
        {
            _delay = delay;
            _rateLimitedCalls = rateLimitedCalls;
            _refuseOnly = refuseOnly;
        }

        public int Calls => Starts.Count;

        public int MaxConcurrency { get; private set; }

        public List<DateTimeOffset> Starts { get; } = [];

        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
        {
            bool refuse;
            lock (_lock)
            {
                Starts.Add(DateTimeOffset.UtcNow);
                _inFlight++;
                MaxConcurrency = Math.Max(MaxConcurrency, _inFlight);
                refuse = _rateLimitedCalls > 0 && (_refuseOnly is null || relativeUrl.Contains(_refuseOnly, StringComparison.Ordinal));
                if (refuse)
                {
                    _rateLimitedCalls--;
                }
            }

            FirstCallStarted.TrySetResult();

            try
            {
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }

                if (refuse)
                {
                    throw new CatalogRateLimitedException();
                }

                return "ok";
            }
            finally
            {
                lock (_lock)
                {
                    _inFlight--;
                }
            }
        }
    }
}
