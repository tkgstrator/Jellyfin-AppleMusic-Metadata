using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Attributes of an <c>albums</c> resource.
/// </summary>
public class AlbumAttributes
{
    /// <summary>
    /// Gets or sets the album title.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the album artist name.
    /// </summary>
    public string? ArtistName { get; set; }

    /// <summary>
    /// Gets or sets the release date, as <c>yyyy-MM-dd</c> or a partial form.
    /// </summary>
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the number of tracks.
    /// </summary>
    public int? TrackCount { get; set; }

    /// <summary>
    /// Gets or sets the record label.
    /// </summary>
    public string? RecordLabel { get; set; }

    /// <summary>
    /// Gets or sets the copyright line.
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// Gets or sets the Universal Product Code.
    /// </summary>
    public string? Upc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a compilation.
    /// </summary>
    public bool? IsCompilation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is a single.
    /// </summary>
    public bool? IsSingle { get; set; }

    /// <summary>
    /// Gets or sets the genre names, localised to the requested language.
    /// </summary>
    public IReadOnlyList<string> GenreNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the editorial notes.
    /// </summary>
    public EditorialNotes? EditorialNotes { get; set; }

    /// <summary>
    /// Gets or sets the artwork descriptor.
    /// </summary>
    public Artwork? Artwork { get; set; }

    /// <summary>
    /// Gets or sets the public Apple Music URL.
    /// </summary>
    public string? Url { get; set; }
}
