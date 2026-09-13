using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;

/// <summary>Explicit, usable embedded values; missing values never request deletion.</summary>
public sealed class EmbeddedMetadata
{
    /// <summary>Gets the track artists.</summary>
    public IReadOnlyList<string> Artists { get; init; } = [];

    /// <summary>Gets the explicitly tagged album artists.</summary>
    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    /// <summary>Gets the album title.</summary>
    public string? Album { get; init; }

    /// <summary>Gets the track number.</summary>
    public int? Track { get; init; }

    /// <summary>Gets the disc number.</summary>
    public int? Disc { get; init; }

    /// <summary>Gets the release year.</summary>
    public int? Year { get; init; }

    /// <summary>Gets the genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets fields with contradictory embedded values.</summary>
    public IReadOnlyList<string> Conflicts { get; init; } = [];

    internal static bool SameText(string? left, string? right)
        => string.Equals(left?.Normalize(NormalizationForm.FormC), right?.Normalize(NormalizationForm.FormC), StringComparison.Ordinal);

    internal static bool SameList(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Count == right.Count && left.Zip(right).All(pair => SameText(pair.First, pair.Second));
}
