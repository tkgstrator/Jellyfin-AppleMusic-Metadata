namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Editorial prose written by Apple Music, localised to the requested language.
/// </summary>
public class EditorialNotes
{
    /// <summary>
    /// Gets or sets the full-length note.
    /// </summary>
    public string? Standard { get; set; }

    /// <summary>
    /// Gets or sets the abridged note.
    /// </summary>
    public string? Short { get; set; }

    /// <summary>
    /// Gets the most complete note available, or null when there is none.
    /// </summary>
    /// <returns>The standard note, falling back to the short note.</returns>
    public string? GetBest() => string.IsNullOrWhiteSpace(Standard) ? Short : Standard;
}
