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
    /// <param name="value">The cached body, or null on a miss. An empty string means "known to be absent".</param>
    /// <returns>True when a live entry was found.</returns>
    bool TryGet(string key, out string? value);

    /// <summary>
    /// Stores a response.
    /// </summary>
    /// <param name="key">Cache key; the request URL.</param>
    /// <param name="value">Response body, or null to record that the resource is absent.</param>
    void Set(string key, string? value);

    /// <summary>
    /// Removes every entry.
    /// </summary>
    void Clear();

    /// <summary>
    /// Writes pending changes to disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the cache is persisted.</returns>
    Task FlushAsync(CancellationToken cancellationToken);
}
