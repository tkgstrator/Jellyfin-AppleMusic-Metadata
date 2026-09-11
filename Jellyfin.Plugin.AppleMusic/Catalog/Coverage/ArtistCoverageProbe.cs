using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// Searches the catalog for every artist name in turn and records whether the
/// name alone identifies the artist.
/// </summary>
/// <remarks>
/// <para>
/// Every name costs one search, the request kind Apple rate limits hardest,
/// so the probe runs strictly sequentially behind the throttled transport and
/// asks for only a handful of results per name. Those small responses fit the
/// on-disk cache, which is what makes the run resumable: a second run after a
/// refusal replays the answered names from the cache and only asks Apple about
/// the rest.
/// </para>
/// <para>
/// The first refusal ends the run. The transport has already paused and
/// retried by then, so the catalog is clearly not answering searches for a
/// while, and every further name would fail fast and be recorded as
/// unanswered — noise, at the cost of more knocking. Other failures — a 5xx
/// from Apple, a dropped connection — are recorded against the name and the
/// run carries on: those are per-request, and a single one must not throw
/// away half an hour of answers.
/// </para>
/// </remarks>
public class ArtistCoverageProbe
{
    /// <summary>
    /// Results requested per name. Enough to find an exact match that is not
    /// ranked first, small enough to persist.
    /// </summary>
    public const int SearchLimit = 5;

    private readonly IAppleMusicCatalog _catalog;
    private readonly ILogger<ArtistCoverageProbe> _logger;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistCoverageProbe"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">Clock; defaults to the system clock.</param>
    public ArtistCoverageProbe(IAppleMusicCatalog catalog, ILogger<ArtistCoverageProbe> logger, TimeProvider? time = null)
    {
        _catalog = catalog;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Checks every name.
    /// </summary>
    /// <param name="names">Distinct artist names.</param>
    /// <param name="progress">Receives the completed fraction in percent.</param>
    /// <param name="checkpoint">
    /// Receives the partial report every <paramref name="checkpointEvery"/>
    /// names, so a long run can be inspected while it is going.
    /// </param>
    /// <param name="checkpointEvery">How many names between checkpoints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    public async Task<ArtistCoverageReport> RunAsync(
        IReadOnlyList<string> names,
        IProgress<double>? progress,
        Func<ArtistCoverageReport, Task>? checkpoint,
        int checkpointEvery,
        CancellationToken cancellationToken)
    {
        var startedAt = _time.GetUtcNow();
        var entries = new List<ArtistCoverageEntry>(names.Count);
        var completed = true;

        for (var i = 0; i < names.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100.0 * i / Math.Max(1, names.Count));

            var name = names[i];
            try
            {
                var results = await _catalog.SearchArtistsAsync(name, SearchLimit, cancellationToken);
                entries.Add(ArtistMatcher.Classify(name, results));
            }
            catch (CatalogRateLimitedException)
            {
                entries.Add(new ArtistCoverageEntry { Name = name, Outcome = ArtistMatchOutcome.RateLimited });
                _logger.LogWarning(
                    "Apple Music refused the artist search for {Name}; stopping the coverage run after {Checked} of {Total} names. Run it again later to continue",
                    name,
                    entries.Count,
                    names.Count);
                completed = false;
                break;
            }
            catch (HttpRequestException ex)
            {
                entries.Add(new ArtistCoverageEntry { Name = name, Outcome = ArtistMatchOutcome.Error, Error = ex.Message });
                _logger.LogWarning(ex, "The artist search for {Name} failed; carrying on with the next name", name);
            }

            if (checkpoint is not null && checkpointEvery > 0 && entries.Count % checkpointEvery == 0 && entries.Count < names.Count)
            {
                await checkpoint(Build(startedAt, names.Count, entries, completed: false));
            }
        }

        progress?.Report(100);
        return Build(startedAt, names.Count, entries, completed);
    }

    private ArtistCoverageReport Build(DateTimeOffset startedAt, int total, List<ArtistCoverageEntry> entries, bool completed)
        => new()
        {
            StartedAt = startedAt,
            UpdatedAt = _time.GetUtcNow(),
            Completed = completed,
            Total = total,
            Entries = entries.ToArray(),
        };
}
