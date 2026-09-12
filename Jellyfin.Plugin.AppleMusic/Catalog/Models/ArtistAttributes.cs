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
    /// Gets or sets the artist biography.
    /// </summary>
    /// <remarks>
    /// Only present when the request asks for it with
    /// <c>extend=artistBio</c>; artists carry no <c>editorialNotes</c>, so
    /// this is the only prose Apple offers for them. May contain
    /// <c>&lt;br&gt;</c>.
    /// </remarks>
    public string? ArtistBio { get; set; }

    /// <summary>
    /// Gets or sets when the artist was born or the group formed, as Apple
    /// writes it in the requested language (for example <c>1991年3月10日</c>
    /// or <c>March 10, 1991</c>). Needs <c>extend=bornOrFormed</c>.
    /// </summary>
    public string? BornOrFormed { get; set; }

    /// <summary>
    /// Gets or sets the artist's country of origin as an ISO code. Needs
    /// <c>extend=origin</c>.
    /// </summary>
    public string? Origin { get; set; }

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
