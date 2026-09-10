using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Supplies the bearer token used by the Apple Music web player.
/// </summary>
public interface IWebPlayTokenProvider
{
    /// <summary>
    /// Gets a currently valid token, fetching or refreshing it as needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bearer token.</returns>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
