using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Carries catalog requests to whatever serves the Apple Music API.
/// </summary>
/// <remarks>
/// Returns the raw body rather than a deserialized object so that a caching
/// decorator can store exactly what came back, with no serialization round
/// trip. Implementations differ only in base URL and how the request is
/// authorised.
/// </remarks>
public interface ICatalogTransport
{
    /// <summary>
    /// Issues a GET request.
    /// </summary>
    /// <param name="relativeUrl">Path and query, starting with '/'.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The response body, or null when the resource was not found or the
    /// request was refused in a way the caller should treat as "no result".
    /// </returns>
    Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken);
}
