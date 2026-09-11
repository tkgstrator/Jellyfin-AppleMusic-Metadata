namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// How a library artist name fared against the catalog.
/// </summary>
public enum ArtistMatchOutcome
{
    /// <summary>
    /// A result carries exactly the same name.
    /// </summary>
    Exact,

    /// <summary>
    /// A result matches once case and surrounding or repeated whitespace are
    /// ignored. Reported separately so the gap to exact matching is visible.
    /// </summary>
    Relaxed,

    /// <summary>
    /// The catalog returned artists, but none with this name.
    /// </summary>
    Unmatched,

    /// <summary>
    /// The catalog returned nothing at all.
    /// </summary>
    NotFound,

    /// <summary>
    /// The catalog refused to answer because of rate limiting.
    /// </summary>
    RateLimited,
}
