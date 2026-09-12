using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.ExternalIds;
using Jellyfin.Plugin.AppleMusic.Organizer;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>
/// Supplies artist images from the Apple Music catalog.
/// </summary>
/// <remarks>
/// Artist images are the main thing a token-free source such as the iTunes
/// Search API cannot provide.
/// </remarks>
public class ArtistImageProvider : IRemoteImageProvider
{
    private readonly IAppleMusicCatalog _catalog;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArtistImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistImageProvider"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public ArtistImageProvider(
        IAppleMusicCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<ArtistImageProvider> logger)
    {
        _catalog = catalog;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClient.GetAsync(new Uri(url), cancellationToken);

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        if (item is not MusicArtist artist)
        {
            return [];
        }

        var options = Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();

        // The stored id first, then the id tagged on the directory; either way
        // no search is needed.
        var id = artist.GetProviderId(ProviderKeys.Artist);
        var storefront = artist.GetProviderId(ProviderKeys.Storefront);
        if (string.IsNullOrEmpty(id))
        {
            id = FolderTag.Parse(Path.GetFileName(artist.Path));
            storefront = null;
        }

        if (!string.IsNullOrEmpty(id))
        {
            var found = await _catalog.GetArtistAsync(id, storefront, cancellationToken);
            var url = ArtworkUrl.Resolve(
                found?.Attributes.Artwork?.Url,
                options.ArtworkSize,
                found?.Attributes.Artwork?.DefaultCropCode);
            return url is null ? [] : [ImageInfo(url, options.ArtworkSize)];
        }

        _logger.LogDebug("Searching Apple Music artist images for {Name}", artist.Name);
        var artists = await _catalog.SearchArtistsAsync(artist.Name, cancellationToken);

        return artists
            .Select(candidate => ArtworkUrl.Resolve(
                candidate.Attributes.Artwork?.Url,
                options.ArtworkSize,
                candidate.Attributes.Artwork?.DefaultCropCode))
            .Where(url => url is not null)
            .Select(url => ImageInfo(url!, options.ArtworkSize))
            .ToList();
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
