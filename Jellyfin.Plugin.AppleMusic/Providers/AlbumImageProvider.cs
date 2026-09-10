using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.ExternalIds;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>
/// Supplies album artwork from the Apple Music catalog.
/// </summary>
public class AlbumImageProvider : IRemoteImageProvider
{
    private readonly IAppleMusicCatalog _catalog;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlbumImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumImageProvider"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AlbumImageProvider(
        IAppleMusicCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<AlbumImageProvider> logger)
    {
        _catalog = catalog;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClient.GetAsync(new Uri(url), cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is not MusicAlbum album)
        {
            return [];
        }

        var options = Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();

        var id = album.GetProviderId(ProviderKeys.Album);
        if (!string.IsNullOrEmpty(id))
        {
            var storefront = album.GetProviderId(ProviderKeys.Storefront);
            var found = await _catalog.GetAlbumAsync(id, storefront, cancellationToken);
            var url = ArtworkUrl.Resolve(found?.Attributes.Artwork?.Url, options.ArtworkSize);
            return url is null ? [] : [ImageInfo(url, options.ArtworkSize)];
        }

        var term = SearchTermFor(album);
        _logger.LogDebug("Searching Apple Music album artwork for {Term}", term);
        var albums = await _catalog.SearchAlbumsAsync(term, cancellationToken);

        return albums
            .Select(candidate => ArtworkUrl.Resolve(candidate.Attributes.Artwork?.Url, options.ArtworkSize))
            .Where(url => url is not null)
            .Select(url => ImageInfo(url!, options.ArtworkSize))
            .ToList();
    }

    private static string SearchTermFor(MusicAlbum album)
    {
        var artist = album.AlbumArtists.Count > 0 ? album.AlbumArtists[0] : string.Empty;
        return string.Join(' ', new[] { artist, album.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static RemoteImageInfo ImageInfo(string url, int size) => new()
    {
        ProviderName = PluginConstants.Name,
        Type = ImageType.Primary,
        Url = url,
        Width = size,
        Height = size,
        ThumbnailUrl = url,
    };
}
