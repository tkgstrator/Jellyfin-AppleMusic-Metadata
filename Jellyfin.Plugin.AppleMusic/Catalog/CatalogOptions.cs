using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Catalog lookup settings. Deliberately free of any Jellyfin dependency so the
/// catalog layer stays unit testable.
/// </summary>
public class CatalogOptions
{
    /// <summary>
    /// Storefront identifier for Japan.
    /// </summary>
    public const string Japan = "jp";

    /// <summary>
    /// Storefront identifier for the United States.
    /// </summary>
    public const string UnitedStates = "us";

    /// <summary>
    /// Gets or sets the storefronts to query, in order. The first one that
    /// yields a result wins.
    /// </summary>
    public IReadOnlyList<string> Storefronts { get; set; } = [Japan, UnitedStates];

    /// <summary>
    /// Gets or sets an explicit Apple Music language tag. When empty, the
    /// language follows the storefront being queried.
    /// </summary>
    public string LanguageOverride { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of results requested per search.
    /// </summary>
    public int MaxSearchResults { get; set; } = 25;

    /// <summary>
    /// Gets or sets the edge length in pixels used when resolving artwork URLs.
    /// </summary>
    public int ArtworkSize { get; set; } = 1400;

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

        return string.Equals(storefront, Japan, StringComparison.OrdinalIgnoreCase)
            ? "ja-jp"
            : "en-us";
    }
}
