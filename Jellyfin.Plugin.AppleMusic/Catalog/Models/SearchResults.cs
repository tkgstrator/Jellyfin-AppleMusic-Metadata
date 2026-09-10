namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Search results, grouped by resource kind. Categories not requested — or
/// requested but empty — are absent.
/// </summary>
public class SearchResults
{
    /// <summary>
    /// Gets or sets the matching songs.
    /// </summary>
    public ResourceList<SongAttributes>? Songs { get; set; }

    /// <summary>
    /// Gets or sets the matching albums.
    /// </summary>
    public ResourceList<AlbumAttributes>? Albums { get; set; }

    /// <summary>
    /// Gets or sets the matching artists.
    /// </summary>
    public ResourceList<ArtistAttributes>? Artists { get; set; }
}
