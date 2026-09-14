namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Settings for the organizer.
/// </summary>
public class OrganizeOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether track files are renamed to
    /// <c>01 Title.ext</c>. Directories are always renamed.
    /// </summary>
    public bool RenameTrackFiles { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the scheduled task only logs
    /// what it would do.
    /// </summary>
    public bool DryRun { get; set; } = true;
}
