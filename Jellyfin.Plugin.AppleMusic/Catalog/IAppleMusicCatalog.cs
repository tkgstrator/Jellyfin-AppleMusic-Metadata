using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Reads the Apple Music catalog, walking the configured storefronts until a
/// result is found.
/// </summary>
public interface IAppleMusicCatalog
{
    /// <summary>
    /// Searches for songs.
    /// </summary>
    /// <param name="term">Search term.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching songs; empty when nothing was found.</returns>
    Task<IReadOnlyList<CatalogItem<SongAttributes>>> SearchSongsAsync(string term, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for albums.
    /// </summary>
    /// <param name="term">Search term.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching albums; empty when nothing was found.</returns>
    Task<IReadOnlyList<CatalogItem<AlbumAttributes>>> SearchAlbumsAsync(string term, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for artists.
    /// </summary>
    /// <param name="term">Search term.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching artists; empty when nothing was found.</returns>
    Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, CancellationToken cancellationToken);

    /// <summary>
    /// Searches for artists, asking for at most <paramref name="limit"/> results.
    /// </summary>
    /// <remarks>
    /// Unlike the other searches this one lets <see cref="CatalogRateLimitedException"/>
    /// through instead of answering "nothing found". Batch tools such as the
    /// artist coverage probe must tell the two apart: an unanswered lookup is
    /// not a missing artist, and once the catalog refuses it is better to stop
    /// than to keep knocking.
    /// </remarks>
    /// <param name="term">Search term.</param>
    /// <param name="limit">Maximum number of results per storefront.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching artists; empty when nothing was found.</returns>
    /// <exception cref="CatalogRateLimitedException">The catalog refused the request.</exception>
    Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Looks a song up by catalog identifier.
    /// </summary>
    /// <param name="id">Apple Music catalog identifier.</param>
    /// <param name="storefront">Storefront the id belongs to; null to try the configured ones in order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The song, or null when it was not found.</returns>
    Task<CatalogItem<SongAttributes>?> GetSongAsync(string id, string? storefront, CancellationToken cancellationToken);

    /// <summary>
    /// Looks an album up by catalog identifier.
    /// </summary>
    /// <param name="id">Apple Music catalog identifier.</param>
    /// <param name="storefront">Storefront the id belongs to; null to try the configured ones in order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The album, or null when it was not found.</returns>
    Task<CatalogItem<AlbumAttributes>?> GetAlbumAsync(string id, string? storefront, CancellationToken cancellationToken);

    /// <summary>
    /// Looks an artist up by catalog identifier.
    /// </summary>
    /// <param name="id">Apple Music catalog identifier.</param>
    /// <param name="storefront">Storefront the id belongs to; null to try the configured ones in order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The artist, or null when it was not found.</returns>
    Task<CatalogItem<ArtistAttributes>?> GetArtistAsync(string id, string? storefront, CancellationToken cancellationToken);
}
