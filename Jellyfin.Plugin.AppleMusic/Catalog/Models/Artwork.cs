namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// Apple Music artwork descriptor. The URL is a template containing
/// <c>{w}</c>, <c>{h}</c> and sometimes <c>{f}</c> placeholders.
/// </summary>
public class Artwork
{
    /// <summary>
    /// Gets or sets the artwork URL template.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the native width of the source image.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the native height of the source image.
    /// </summary>
    public int? Height { get; set; }

    /// <summary>
    /// Gets or sets the average background colour, as a hex triplet without '#'.
    /// </summary>
    public string? BgColor { get; set; }
}
