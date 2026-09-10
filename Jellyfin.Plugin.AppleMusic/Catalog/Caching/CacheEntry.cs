using System;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// One cached response.
/// </summary>
public class CacheEntry
{
    /// <summary>
    /// Gets or sets the response body. Null means the resource is known to be
    /// absent, which is worth remembering so the lookup is not repeated.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Gets or sets the instant after which this entry must not be used.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Gets a value indicating whether this entry is still usable.
    /// </summary>
    public bool IsLive => DateTimeOffset.UtcNow < ExpiresAt;
}
