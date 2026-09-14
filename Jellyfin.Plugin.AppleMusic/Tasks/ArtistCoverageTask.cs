using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Coverage;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Tasks;

/// <summary>
/// Measures how many of the library's artist names Apple Music identifies by
/// name alone, and writes the report for the configuration page.
/// </summary>
/// <remarks>
/// No default trigger: every name costs one rate-limited search, so this is
/// something the user starts deliberately, from the task list or the
/// configuration page. The run stops at the first refusal and continues from
/// the cache when started again.
/// </remarks>
public class ArtistCoverageTask : IScheduledTask
{
    private readonly ArtistCoverageRunner _runner;
    private readonly ILogger<ArtistCoverageTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistCoverageTask"/> class.
    /// </summary>
    /// <param name="runner">Runner.</param>
    /// <param name="logger">Logger.</param>
    public ArtistCoverageTask(ArtistCoverageRunner runner, ILogger<ArtistCoverageTask> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Check artist coverage on Apple Music";

    /// <inheritdoc />
    public string Key => "AppleMusicArtistCoverage";

    /// <inheritdoc />
    public string Description
        => "Searches Apple Music for every artist name in the library and reports how many are identified by name alone. One search per artist; stops when Apple starts refusing and resumes from the cache next time.";

    /// <inheritdoc />
    public string Category => PluginConstants.Name;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _runner.RunAsync(progress, cancellationToken);
            _logger.LogInformation(
                "Artist coverage: {Exact} exact and {Relaxed} relaxed matches out of {Checked} of {Total} names ({ExactPercent}% exact){Stopped}",
                report.Exact,
                report.Relaxed,
                report.Checked,
                report.Total,
                report.ExactPercent,
                report.Completed ? string.Empty : "; stopped early because Apple Music refused to answer");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Artist coverage task skipped: {Reason}", ex.Message);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];
}
