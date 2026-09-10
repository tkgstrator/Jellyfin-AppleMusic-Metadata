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
    /// Gets or sets how long a successful response stays usable. Catalog
    /// metadata barely changes, so this can be generous.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets or sets how long a "not found" answer is remembered. Shorter than
    /// <see cref="Lifetime"/>, because a track missing today may be added
    /// tomorrow.
    /// </summary>
    public TimeSpan NegativeLifetime { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Gets or sets the maximum number of entries kept. When exceeded, the
    /// entries closest to expiry are dropped first.
    /// </summary>
    public int MaxEntries { get; set; } = 20000;
}
