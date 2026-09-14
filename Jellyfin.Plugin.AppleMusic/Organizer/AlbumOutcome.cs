using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// What happened to one album, for the grouped view of a report.
/// </summary>
public class AlbumOutcome
{
    /// <summary>
    /// Gets the album name as Jellyfin has it.
    /// </summary>
    public string Album { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Apple Music identifier of the album.
    /// </summary>
    public string AlbumId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the album directory name after the move. Empty when the album is
    /// already where it belongs.
    /// </summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many track files are renamed.
    /// </summary>
    public int TrackRenames { get; init; }

    /// <summary>
    /// Gets a value indicating whether the album directory itself moves.
    /// </summary>
    public bool DirectoryMoves { get; init; }

    /// <summary>
    /// Gets what was left alone in this album, with reasons.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];
}
