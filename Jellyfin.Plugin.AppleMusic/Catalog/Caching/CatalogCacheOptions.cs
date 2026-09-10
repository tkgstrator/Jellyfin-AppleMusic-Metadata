using System;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// Cache tuning, kept free of any Jellyfin dependency.
/// </summary>
public class CatalogCacheOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether responses are cached at all.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets how long a cached response stays usable. Catalog metadata
    /// barely changes, so this can be generous.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets how long a "not found" answer is remembered. Shorter than
    /// <see cref="Lifetime"/>, because a track missing today may be added
    /// tomorrow.
    /// </summary>
    public TimeSpan NegativeLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the memory budget in bytes. The least recently used entries
    /// are evicted once it is exceeded.
    /// </summary>
    /// <remarks>
    /// A byte budget rather than an entry count, because entry sizes differ by
    /// more than an order of magnitude: a search response runs to ~31 KB while
    /// an id lookup is ~1.5 KB. Counting entries would make the real memory
    /// usage depend on which kind happened to be cached.
    /// </remarks>
    public long MaxMemoryBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the largest entry written to disk. Bigger responses stay in
    /// memory only.
    /// </summary>
    /// <remarks>
    /// This is what keeps the on-disk cache bounded on a large library. Search
    /// responses are large and rarely replayed — once an id has been resolved,
    /// Jellyfin stores it on the item and every later lookup is by id. Id
    /// lookups are small and repeat often, so those are the ones worth keeping.
    /// </remarks>
    public int MaxPersistedEntryBytes { get; set; } = 8 * 1024;
}
