using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Attributes of an <c>artists</c> resource.
/// </summary>
public class ArtistAttributes
{
    /// <summary>
    /// Gets or sets the artist name.
    /// </summary>
    public string? Name { get; set; }

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
