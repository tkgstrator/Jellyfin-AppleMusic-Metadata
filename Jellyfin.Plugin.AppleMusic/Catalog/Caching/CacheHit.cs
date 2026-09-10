namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// A cache lookup that found something. A null <see cref="Body"/> is a
/// meaningful answer: the resource is known to be absent.
/// </summary>
public class CacheHit
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheHit"/> class.
    /// </summary>
    /// <param name="body">Cached response body, or null for a known absence.</param>
    public CacheHit(string? body)
    {
        Body = body;
    }

    /// <summary>
    /// Gets the cached response body.
    /// </summary>
    public string? Body { get; }
}
