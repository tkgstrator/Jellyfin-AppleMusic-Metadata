using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Api;

/// <summary>
/// Cache controls for the plugin's configuration page.
/// </summary>
[ApiController]
[Route("AppleMusic")]
[Authorize(Policy = "RequiresElevation")]
public class AppleMusicController : ControllerBase
{
    private readonly ICatalogCache _cache;
    private readonly ILogger<AppleMusicController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicController"/> class.
    /// </summary>
    /// <param name="cache">Catalog cache.</param>
    /// <param name="logger">Logger.</param>
    public AppleMusicController(ICatalogCache cache, ILogger<AppleMusicController> logger)
    {
        _cache = cache;
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
