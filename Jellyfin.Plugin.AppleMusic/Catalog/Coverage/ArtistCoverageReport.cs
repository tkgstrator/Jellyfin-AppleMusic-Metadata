using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// Outcome of an artist coverage run: how many of the library's artist names
/// the catalog can identify by name alone.
/// </summary>
public class ArtistCoverageReport
{
    /// <summary>
    /// Gets when the run started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets when the report was last written. Advances on every checkpoint of
    /// a run in progress.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether every name was checked. False while the
    /// run is in progress and when it stopped early because the catalog
    /// refused to answer.
    /// </summary>
    public bool Completed { get; init; }

    /// <summary>
    /// Gets how many distinct artist names the library has.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Gets the names checked so far, in the order they were checked.
    /// </summary>
    public IReadOnlyList<ArtistCoverageEntry> Entries { get; init; } = [];

    /// <summary>
    /// Gets how many names have been checked.
    /// </summary>
    public int Checked => Entries.Count;

    /// <summary>
    /// Gets how many names a result matched exactly.
    /// </summary>
    public int Exact => Count(ArtistMatchOutcome.Exact);

    /// <summary>
    /// Gets how many names matched only when case and whitespace are ignored.
    /// </summary>
    public int Relaxed => Count(ArtistMatchOutcome.Relaxed);

    /// <summary>
    /// Gets how many names had results but no match.
    /// </summary>
    public int Unmatched => Count(ArtistMatchOutcome.Unmatched);

    /// <summary>
    /// Gets how many names had no result at all.
    /// </summary>
    public int NotFound => Count(ArtistMatchOutcome.NotFound);

    /// <summary>
    /// Gets how many names went unanswered because of rate limiting.
    /// </summary>
    public int RateLimited => Count(ArtistMatchOutcome.RateLimited);

    /// <summary>
    /// Gets the share of checked names that matched exactly, in percent.
    /// </summary>
    public double ExactPercent => Percent(Exact);

    /// <summary>
    /// Gets the share of checked names that matched exactly or relaxed, in percent.
    /// </summary>
    public double RelaxedPercent => Percent(Exact + Relaxed);

    private int Count(ArtistMatchOutcome outcome)
        => Entries.Count(entry => entry.Outcome == outcome);

    private double Percent(int count)
    {
        var answered = Checked - RateLimited;
        return answered == 0 ? 0 : Math.Round(100.0 * count / answered, 1);
    }
}
