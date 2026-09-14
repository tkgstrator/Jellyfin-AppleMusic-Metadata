namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// The <c>relationships</c> block of a resource. Apple includes it on id
/// lookups: a song points at its album and artists, an album at its artists
/// and its full track list.
/// </summary>
public class Relationships
{
    /// <summary>
    /// Gets or sets the albums a song belongs to. Only ids are populated.
    /// </summary>
    public ResourceList<AlbumAttributes>? Albums { get; set; }

    /// <summary>
    /// Gets or sets the artists of a song or album. Only ids are populated.
    /// </summary>
    public ResourceList<ArtistAttributes>? Artists { get; set; }

    /// <summary>
    /// Gets or sets the tracks of an album, with full attributes.
    /// </summary>
    public TrackList? Tracks { get; set; }
}
