namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// What is being moved.
/// </summary>
public enum MoveKind
{
    /// <summary>
    /// A whole directory, contents included.
    /// </summary>
    Directory,

    /// <summary>
    /// A single file.
    /// </summary>
    File,
}

/// <summary>
/// One rename the organizer intends to perform.
/// </summary>
/// <param name="Kind">Whether a directory or a file moves.</param>
/// <param name="From">
/// Current path. For a file this is the path <em>after</em> the directory
/// moves listed before it have been applied.
/// </param>
/// <param name="To">Target path.</param>
public sealed record PlannedMove(MoveKind Kind, string From, string To);
