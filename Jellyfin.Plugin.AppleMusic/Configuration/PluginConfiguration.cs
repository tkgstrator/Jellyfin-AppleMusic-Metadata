using System.Collections.Generic;
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
    /// Storefront identifier for Japan.
    /// </summary>
    public const string JapanStorefront = "jp";

    /// <summary>
    /// Storefront identifier for the United States.
    /// </summary>
    public const string UnitedStatesStorefront = "us";

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
    /// Gets or sets the base URL of the self-hosted Apple Music backend,
    /// e.g. <c>https://applemusic.example.com</c>. The backend is expected to
    /// proxy the Apple Music API and return its responses verbatim.
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
    /// Gets or sets the per-request timeout in seconds for backend calls.
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
            StorefrontPriority.JapanThenUnitedStates => [JapanStorefront, UnitedStatesStorefront],
            StorefrontPriority.UnitedStatesThenJapan => [UnitedStatesStorefront, JapanStorefront],
            StorefrontPriority.JapanOnly => [JapanStorefront],
            StorefrontPriority.UnitedStatesOnly => [UnitedStatesStorefront],
            _ => [JapanStorefront, UnitedStatesStorefront]
        };
    }

    /// <summary>
    /// Gets the Apple Music language tag to use for a storefront.
    /// </summary>
    /// <param name="storefront">Storefront identifier.</param>
    /// <returns>Language tag for the Apple Music <c>l</c> parameter.</returns>
    public string GetLanguageFor(string storefront)
    {
        if (!string.IsNullOrWhiteSpace(LanguageOverride))
        {
            return LanguageOverride;
        }

        return storefront == JapanStorefront ? "ja-jp" : "en-us";
    }
}
