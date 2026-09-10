using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Jellyfin.Plugin.AppleMusic.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class CacheMaintenanceTaskTests
{
    [Fact]
    public async Task ExecuteAsync_PrunesAndReportsCompletion()
    {
        var cache = new RecordingCache();
        var progress = new RecordingProgress();
        var task = new CacheMaintenanceTask(cache, NullLogger<CacheMaintenanceTask>.Instance);

        await task.ExecuteAsync(progress, TestContext.Current.CancellationToken);

        Assert.Equal(1, cache.PruneCalls);
        Assert.Equal(100, progress.Reported.Last());
    }

    [Fact]
    public void GetDefaultTriggers_RunsWeekly()
    {
        var task = new CacheMaintenanceTask(new RecordingCache(), NullLogger<CacheMaintenanceTask>.Instance);

        var trigger = Assert.Single(task.GetDefaultTriggers());

        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromDays(7).Ticks, trigger.IntervalTicks);
    }

    [Fact]
    public void Key_IsStable()
    {
        // Jellyfin keys stored trigger overrides by this value; changing it
        // silently resets whatever schedule the user configured.
        var task = new CacheMaintenanceTask(new RecordingCache(), NullLogger<CacheMaintenanceTask>.Instance);

        Assert.Equal("AppleMusicCachePrune", task.Key);
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Reported { get; } = [];

        public void Report(double value) => Reported.Add(value);
    }

    private sealed class RecordingCache : ICatalogCache
    {
        public int PruneCalls { get; private set; }

        public ValueTask<CacheHit?> GetAsync(string key, CancellationToken cancellationToken)
            => ValueTask.FromResult<CacheHit?>(null);

        public ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public void Clear()
        {
        }

        public Task<int> PruneAsync(CancellationToken cancellationToken)
        {
            PruneCalls++;
            return Task.FromResult(3);
        }
    }
}
