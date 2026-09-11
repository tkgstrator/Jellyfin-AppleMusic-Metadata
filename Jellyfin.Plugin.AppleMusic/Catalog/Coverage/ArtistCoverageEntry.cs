namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// One library artist name and what the catalog said about it.
/// </summary>
public class ArtistCoverageEntry
{
    /// <summary>
    /// Gets the artist name as Jellyfin has it.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the outcome.
    /// </summary>
    public ArtistMatchOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the name of the matched artist, or of the first result when none
    /// matched, so a reader can see why. Null when there was no result.
    /// </summary>
    public string? Candidate { get; init; }

    /// <summary>
    /// Gets the catalog identifier of <see cref="Candidate"/>.
    /// </summary>
    public string? CandidateId { get; init; }

    /// <summary>
    /// Gets the storefront <see cref="CandidateId"/> belongs to.
    /// </summary>
    public string? Storefront { get; init; }
}
