using System;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Throttling;

/// <summary>
/// Settings for <see cref="ThrottledCatalogTransport"/>.
/// </summary>
public class ThrottleOptions
{
    /// <summary>
    /// Gets or sets the minimum time between two requests leaving the plugin.
    /// </summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how long to pause after the first rate-limit response. Each
    /// consecutive one doubles the pause, up to <see cref="MaxCooldown"/>.
    /// </summary>
    public TimeSpan InitialCooldown { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the longest pause between attempts.
    /// </summary>
    public TimeSpan MaxCooldown { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how many times a single lookup is attempted before giving up.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;
}
