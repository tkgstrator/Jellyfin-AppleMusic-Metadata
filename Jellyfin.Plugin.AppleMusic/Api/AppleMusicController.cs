using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Jellyfin.Plugin.AppleMusic.Organizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Api;

/// <summary>
/// Cache and organizer controls for the plugin's configuration page.
/// </summary>
[ApiController]
[Route("AppleMusic")]
[Authorize(Policy = "RequiresElevation")]
public class AppleMusicController : ControllerBase
{
    private readonly ICatalogCache _cache;
    private readonly LibraryOrganizer _organizer;
    private readonly ILogger<AppleMusicController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicController"/> class.
    /// </summary>
    /// <param name="cache">Catalog cache.</param>
    /// <param name="organizer">Library organizer.</param>
    /// <param name="logger">Logger.</param>
    public AppleMusicController(ICatalogCache cache, LibraryOrganizer organizer, ILogger<AppleMusicController> logger)
    {
        _cache = cache;
        _organizer = organizer;
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
