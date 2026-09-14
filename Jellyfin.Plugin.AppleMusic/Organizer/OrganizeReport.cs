using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Outcome of a planning or organizing run.
/// </summary>
public class OrganizeReport
{
    /// <summary>
    /// Gets a value indicating whether nothing was touched on disk.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Gets the renames that were planned.
    /// </summary>
    public IReadOnlyList<PlannedMove> Moves { get; init; } = [];

    /// <summary>
    /// Gets what was left alone, with reasons.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>
    /// Gets the renames that failed, with reasons. Empty on a dry run.
    /// </summary>
    public IReadOnlyList<string> Failed { get; init; } = [];

    /// <summary>
    /// Gets how many renames were applied. Zero on a dry run.
    /// </summary>
    public int Applied { get; init; }

    /// <summary>
    /// Gets how many albums carried an Apple Music id and were considered.
    /// </summary>
    public int Albums { get; init; }
}
