using System;
using System.Globalization;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Resolves Apple Music artwork URL templates.
/// </summary>
/// <remarks>
/// Templates look like <c>https://.../{w}x{h}bb.jpg</c>, and sometimes carry
/// <c>{c}</c> (crop code) and <c>{f}</c> (format) placeholders as well.
/// </remarks>
public static class ArtworkUrl
{
    private const string DefaultCropCode = "bb";
    private const string DefaultFormat = "jpg";

    /// <summary>
    /// Substitutes the size placeholders in an artwork template.
    /// </summary>
    /// <param name="template">Artwork URL template. May be null or empty.</param>
    /// <param name="size">Edge length in pixels, used for both width and height.</param>
    /// <returns>A concrete URL, or null when there is no usable template.</returns>
    public static string? Resolve(string? template, int size)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Artwork size must be positive.");
        }

        var edge = size.ToString(CultureInfo.InvariantCulture);
        return template
            .Replace("{w}", edge, StringComparison.Ordinal)
            .Replace("{h}", edge, StringComparison.Ordinal)
            .Replace("{c}", DefaultCropCode, StringComparison.Ordinal)
            .Replace("{f}", DefaultFormat, StringComparison.Ordinal);
    }
}
