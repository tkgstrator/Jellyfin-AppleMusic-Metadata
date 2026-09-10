namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Response envelope of <c>/v1/catalog/{storefront}/search</c>.
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// Gets or sets the per-category results.
    /// </summary>
    public SearchResults? Results { get; set; }
}
