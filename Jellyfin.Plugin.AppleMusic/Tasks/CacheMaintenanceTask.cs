using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Tasks;

/// <summary>
/// Sweeps expired entries out of the catalog cache.
/// </summary>
/// <remarks>
/// Reads already ignore expired entries, but nothing deletes them, so the cache
/// directory would grow without bound on a large library. Weekly is plenty
/// given the default 30 day lifetime.
/// </remarks>
public class CacheMaintenanceTask : IScheduledTask
{
    private readonly ICatalogCache _cache;
    private readonly ILogger<CacheMaintenanceTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheMaintenanceTask"/> class.
    /// </summary>
    /// <param name="cache">Catalog cache.</param>
    /// <param name="logger">Logger.</param>
    public CacheMaintenanceTask(ICatalogCache cache, ILogger<CacheMaintenanceTask> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Prune the Apple Music cache";

    /// <inheritdoc />
    public string Key => "AppleMusicCachePrune";

    /// <inheritdoc />
    public string Description => "Deletes expired Apple Music catalog responses from disk.";

    /// <inheritdoc />
    public string Category => PluginConstants.Name;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        var removed = await _cache.PruneAsync(cancellationToken);
        _logger.LogInformation("Pruned {Count} expired Apple Music cache entries", removed);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromDays(7).Ticks,
        };
    }
}
