using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Carries catalog requests to whatever serves the Apple Music API.
/// </summary>
/// <remarks>
/// Implementations differ only in base URL and how the request is authorised:
/// the web player token against <c>amp-api</c>, or an API key against a
/// self-hosted backend. Response shapes are identical either way.
/// </remarks>
public interface ICatalogTransport
{
    /// <summary>
    /// Issues a GET request and deserializes the response.
    /// </summary>
    /// <typeparam name="T">Expected response type.</typeparam>
    /// <param name="relativeUrl">Path and query, starting with '/'.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response, or null when the resource was not found.</returns>
    Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        where T : class;
}
