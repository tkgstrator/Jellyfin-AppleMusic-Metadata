using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;

/// <summary>Library rules detached from the server's configuration types.</summary>
public sealed class EmbeddedTagOptions
{
    /// <summary>Gets a value indicating whether ARTISTS and ALBUMARTISTS take precedence.</summary>
    public bool PreferNonstandardArtists { get; init; }

    /// <summary>Gets explicitly enabled custom delimiters.</summary>
    public IReadOnlyList<string> Delimiters { get; init; } = [];

    /// <summary>Gets names whose delimiter characters must remain literal.</summary>
    public IReadOnlyList<string> Whitelist { get; init; } = [];
}
