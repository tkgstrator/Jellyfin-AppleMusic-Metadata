using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Organizer;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Tasks;

/// <summary>
/// Moves matched albums into the <c>Artist-[amid-id]/Album-[amid-id]</c> layout.
/// </summary>
/// <remarks>
/// No default trigger: renaming a library is something the user schedules
/// deliberately, after checking a dry run. While <c>Dry run</c> is on in the
/// settings the task only logs what it would do.
/// </remarks>
public class LibraryOrganizeTask : IScheduledTask
{
    private readonly LibraryOrganizer _organizer;
    private readonly ILogger<LibraryOrganizeTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryOrganizeTask"/> class.
    /// </summary>
    /// <param name="organizer">Organizer.</param>
    /// <param name="logger">Logger.</param>
    public LibraryOrganizeTask(LibraryOrganizer organizer, ILogger<LibraryOrganizeTask> logger)
    {
        _organizer = organizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Organize the library by Apple Music ids";

    /// <inheritdoc />
    public string Key => "AppleMusicOrganize";

    /// <inheritdoc />
    public string Description
        => "Renames matched artist and album directories to include their Apple Music ids, and track files to their catalog titles. Honours the plugin's Dry run setting.";

    /// <inheritdoc />
    public string Category => PluginConstants.Name;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var dryRun = Plugin.Instance?.Configuration.OrganizeDryRun ?? true;
        try
        {
            var report = await _organizer.RunAsync(dryRun, progress, cancellationToken);
            _logger.LogInformation(
                "Organize task finished: {Albums} album(s), {Moves} move(s) planned, {Applied} applied, {Skipped} skipped, {Failed} failed{DryRun}",
                report.Albums,
                report.Moves.Count,
                report.Applied,
                report.Skipped.Count,
                report.Failed.Count,
                report.DryRun ? " (dry run)" : string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Organize task skipped: {Reason}", ex.Message);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];
}
