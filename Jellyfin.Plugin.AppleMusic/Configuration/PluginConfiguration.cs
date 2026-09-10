using System;
using System.Collections.Generic;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Jellyfin.Plugin.AppleMusic.Catalog.Throttling;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.AppleMusic.Configuration;

/// <summary>
/// Order in which Apple Music storefronts are queried.
/// </summary>
public enum StorefrontPriority
{
    /// <summary>
    /// Query the Japanese storefront first, fall back to the US storefront.
    /// </summary>
    JapanThenUnitedStates,

    /// <summary>
    /// Query the US storefront first, fall back to the Japanese storefront.
    /// </summary>
    UnitedStatesThenJapan,

    /// <summary>
    /// Query the Japanese storefront only.
    /// </summary>
    JapanOnly,

    /// <summary>
    /// Query the US storefront only.
    /// </summary>
    UnitedStatesOnly
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        BackendBaseUrl = string.Empty;
        BackendApiKey = string.Empty;
        Storefronts = StorefrontPriority.JapanThenUnitedStates;
        LanguageOverride = string.Empty;
        MaxSearchResults = 25;
        ArtworkSize = 1400;
        RequestTimeoutSeconds = 30;
        RequestIntervalMilliseconds = 1000;
        EnableCache = true;
        CacheLifetimeDays = 30;
        CacheNotFoundLifetimeHours = 24;
        MaxCacheMemoryMegabytes = 64;
        MaxPersistedEntryKilobytes = 8;
    }

    /// <summary>
    /// Gets or sets the base URL of a self-hosted Apple Music backend. Reserved
    /// for the backend transport; leave empty to use the web player token.
    /// </summary>
    public string BackendBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the API key sent to the backend in the
    /// <c>X-Api-Key</c> header. Leave empty if the backend needs no auth.
    /// </summary>
    public string BackendApiKey { get; set; }

    /// <summary>
    /// Gets or sets the storefront query order.
    /// </summary>
    public StorefrontPriority Storefronts { get; set; }

    /// <summary>
    /// Gets or sets an explicit language tag (Apple Music <c>l</c> parameter)
    /// such as <c>ja-jp</c> or <c>en-us</c>. When empty, the language is
    /// derived from the storefront being queried.
    /// </summary>
    public string LanguageOverride { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of search results requested per query.
    /// </summary>
    public int MaxSearchResults { get; set; }

    /// <summary>
    /// Gets or sets the edge length in pixels used when resolving Apple Music
    /// artwork URL templates.
    /// </summary>
    public int ArtworkSize { get; set; }

    /// <summary>
    /// Gets or sets the per-request timeout in seconds for catalog calls.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the minimum time between two catalog requests, in
    /// milliseconds. Apple limits the search endpoint per IP address and keeps
    /// refusing for a long time once tripped, so requests are never sent in
    /// parallel and are spaced out by at least this much.
    /// </summary>
    public int RequestIntervalMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether catalog responses are cached
    /// locally so the same lookup is not fetched twice.
    /// </summary>
    public bool EnableCache { get; set; }

    /// <summary>
    /// Gets or sets how many days a cached response stays usable.
    /// </summary>
    public int CacheLifetimeDays { get; set; }

    /// <summary>
    /// Gets or sets how many hours a "not found" answer is remembered.
    /// </summary>
    public int CacheNotFoundLifetimeHours { get; set; }

    /// <summary>
    /// Gets or sets the memory budget for cached responses, in megabytes.
    /// </summary>
    public int MaxCacheMemoryMegabytes { get; set; }

    /// <summary>
    /// Gets or sets the largest response written to disk, in kilobytes.
    /// Larger ones are kept in memory only.
    /// </summary>
    public int MaxPersistedEntryKilobytes { get; set; }

    /// <summary>
    /// Gets the storefronts to query, in order.
    /// </summary>
    /// <returns>Ordered storefront identifiers.</returns>
    public IReadOnlyList<string> GetStorefrontOrder()
    {
        return Storefronts switch
        {
            StorefrontPriority.JapanThenUnitedStates => [CatalogOptions.Japan, CatalogOptions.UnitedStates],
            StorefrontPriority.UnitedStatesThenJapan => [CatalogOptions.UnitedStates, CatalogOptions.Japan],
            StorefrontPriority.JapanOnly => [CatalogOptions.Japan],
            StorefrontPriority.UnitedStatesOnly => [CatalogOptions.UnitedStates],
            _ => [CatalogOptions.Japan, CatalogOptions.UnitedStates]
        };
    }

    /// <summary>
    /// Projects this configuration onto the Jellyfin-independent options used by
    /// the catalog layer.
    /// </summary>
    /// <returns>Catalog options.</returns>
    public CatalogOptions ToCatalogOptions()
    {
        return new CatalogOptions
        {
            Storefronts = GetStorefrontOrder(),
            LanguageOverride = LanguageOverride,
            MaxSearchResults = MaxSearchResults,
            ArtworkSize = ArtworkSize,
        };
    }

    /// <summary>
    /// Projects the request pacing settings onto the options used by the throttle.
    /// </summary>
    /// <returns>Throttle options.</returns>
    public ThrottleOptions ToThrottleOptions()
    {
        return new ThrottleOptions
        {
            MinInterval = TimeSpan.FromMilliseconds(Math.Max(0, RequestIntervalMilliseconds)),
        };
    }

    /// <summary>
    /// Projects the cache settings onto the options used by the cache itself.
    /// </summary>
    /// <returns>Cache options.</returns>
    public CatalogCacheOptions ToCacheOptions()
    {
        return new CatalogCacheOptions
        {
            Enabled = EnableCache,
            Lifetime = TimeSpan.FromDays(Math.Max(1, CacheLifetimeDays)),
            NegativeLifetime = TimeSpan.FromHours(Math.Max(1, CacheNotFoundLifetimeHours)),
            MaxMemoryBytes = Math.Max(1, MaxCacheMemoryMegabytes) * 1024L * 1024L,
            MaxPersistedEntryBytes = Math.Max(0, MaxPersistedEntryKilobytes) * 1024,
        };
    }
}
