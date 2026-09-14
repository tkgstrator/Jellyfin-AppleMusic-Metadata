using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Jellyfin.Plugin.AppleMusic.Catalog.Coverage;
using Jellyfin.Plugin.AppleMusic.Coverage;
using Jellyfin.Plugin.AppleMusic.Organizer;
using Jellyfin.Plugin.AppleMusic.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Api;

/// <summary>
/// Cache, organizer and coverage controls for the plugin's configuration page.
/// </summary>
[ApiController]
[Route("AppleMusic")]
[Authorize(Policy = "RequiresElevation")]
public class AppleMusicController : ControllerBase
{
    private readonly ICatalogCache _cache;
    private readonly LibraryOrganizer _organizer;
    private readonly ArtistCoverageRunner _coverage;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<AppleMusicController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicController"/> class.
    /// </summary>
    /// <param name="cache">Catalog cache.</param>
    /// <param name="organizer">Library organizer.</param>
    /// <param name="coverage">Artist coverage runner.</param>
    /// <param name="taskManager">Scheduled task manager.</param>
    /// <param name="logger">Logger.</param>
    public AppleMusicController(
        ICatalogCache cache,
        LibraryOrganizer organizer,
        ArtistCoverageRunner coverage,
        ITaskManager taskManager,
        ILogger<AppleMusicController> logger)
    {
        _cache = cache;
        _organizer = organizer;
        _coverage = coverage;
        _taskManager = taskManager;
        _logger = logger;
    }

    /// <summary>
    /// Discards every cached catalog response.
    /// </summary>
    /// <response code="204">Cache cleared.</response>
    /// <returns>No content.</returns>
    [HttpPost("Cache/Clear")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult ClearCache()
    {
        _logger.LogInformation("Clearing the Apple Music catalog cache on request");
        _cache.Clear();
        return NoContent();
    }

    /// <summary>
    /// Reports what the organizer would move, without touching anything.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The planned moves.</response>
    /// <response code="409">The organizer is already running.</response>
    /// <returns>The plan.</returns>
    [HttpPost("Organize/Plan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizeReport>> PlanOrganize(CancellationToken cancellationToken)
    {
        try
        {
            return await _organizer.RunAsync(dryRun: true, progress: null, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Moves matched albums into the Apple Music id layout. Ignores the Dry
    /// run setting: the button on the settings page confirms first.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">What was moved.</response>
    /// <response code="409">The organizer is already running.</response>
    /// <returns>The report.</returns>
    [HttpPost("Organize/Apply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizeReport>> ApplyOrganize(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Organizing the library on request");
        try
        {
            return await _organizer.RunAsync(dryRun: false, progress: null, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// Returns the latest artist coverage report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The report; partial while a run is in progress.</response>
    /// <response code="404">No run has happened yet.</response>
    /// <returns>The report.</returns>
    [HttpGet("Artists/Coverage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArtistCoverageReport>> GetArtistCoverage(CancellationToken cancellationToken)
    {
        var report = await _coverage.LoadReportAsync(cancellationToken);
        return report is null ? NotFound() : report;
    }

    /// <summary>
    /// Starts the artist coverage task in the background. Read the result
    /// with <see cref="GetArtistCoverage"/>.
    /// </summary>
    /// <response code="202">The task was queued.</response>
    /// <response code="409">A run is already in progress.</response>
    /// <returns>Accepted.</returns>
    [HttpPost("Artists/Coverage/Run")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult RunArtistCoverage()
    {
        if (_coverage.IsRunning)
        {
            return Conflict("An artist coverage run is already in progress.");
        }

        _logger.LogInformation("Starting the artist coverage task on request");
        _taskManager.Execute<ArtistCoverageTask>();
        return Accepted();
    }

    /// <summary>
    /// Deletes expired entries from the cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Number of entries removed.</response>
    /// <returns>How many entries were removed.</returns>
    [HttpPost("Cache/Prune")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> PruneCache(CancellationToken cancellationToken)
    {
        var removed = await _cache.PruneAsync(cancellationToken);
        _logger.LogInformation("Pruned {Count} expired Apple Music cache entries on request", removed);
        return removed;
    }
}
