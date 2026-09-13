using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;

/// <summary>Reads explicit tags without depending on Jellyfin metadata entities.</summary>
public interface IEmbeddedMetadataReader
{
    /// <summary>Reads a local file without modifying it.</summary>
    /// <param name="path">Local audio path.</param>
    /// <param name="options">Library tag interpretation rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Usable embedded fields and conflicts.</returns>
    Task<EmbeddedMetadata> ReadAsync(string path, EmbeddedTagOptions options, CancellationToken cancellationToken);
}
