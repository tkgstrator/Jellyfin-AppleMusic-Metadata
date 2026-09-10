using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Attributes of a <c>songs</c> resource.
/// </summary>
public class SongAttributes
{
    /// <summary>
    /// Gets or sets the track title.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the performing artist name.
    /// </summary>
    public string? ArtistName { get; set; }

    /// <summary>
    /// Gets or sets the album artist name.
    /// </summary>
    public string? AlbumArtistName { get; set; }

    /// <summary>
    /// Gets or sets the album title.
    /// </summary>
    public string? AlbumName { get; set; }

    /// <summary>
    /// Gets or sets the composer name.
    /// </summary>
    public string? ComposerName { get; set; }

    /// <summary>
    /// Gets or sets the track number within its disc.
    /// </summary>
    public int? TrackNumber { get; set; }

    /// <summary>
    /// Gets or sets the disc number within its album.
    /// </summary>
    public int? DiscNumber { get; set; }

    /// <summary>
    /// Gets or sets the track duration in milliseconds.
    /// </summary>
    public long? DurationInMillis { get; set; }

    /// <summary>
    /// Gets or sets the release date, as <c>yyyy-MM-dd</c> or a partial form.
    /// </summary>
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the genre names, localised to the requested language.
    /// </summary>
    public IReadOnlyList<string> GenreNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the International Standard Recording Code.
    /// </summary>
    public string? Isrc { get; set; }

    /// <summary>
    /// Gets or sets the artwork descriptor.
    /// </summary>
    public Artwork? Artwork { get; set; }

    /// <summary>
    /// Gets or sets the public Apple Music URL.
    /// </summary>
    public string? Url { get; set; }
}
