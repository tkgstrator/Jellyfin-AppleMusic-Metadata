using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// Stores catalog responses so the same lookup is not fetched twice.
/// </summary>
public interface ICatalogCache
{
    /// <summary>
    /// Reads a cached response.
    /// </summary>
    /// <param name="key">Cache key; the request URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hit, or null on a miss.</returns>
    ValueTask<CacheHit?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a response.
    /// </summary>
    /// <param name="key">Cache key; the request URL.</param>
    /// <param name="value">Response body, or null to record that the resource is absent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the entry has been stored.</returns>
    ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken);

    /// <summary>
    /// Removes every entry, from memory and from disk.
    /// </summary>
    void Clear();
}
