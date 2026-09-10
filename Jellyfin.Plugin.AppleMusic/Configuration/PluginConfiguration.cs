using System.Collections.Generic;
using Jellyfin.Plugin.AppleMusic.Catalog;
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
}
