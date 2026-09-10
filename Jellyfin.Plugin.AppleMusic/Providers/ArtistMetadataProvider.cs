using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
using Jellyfin.Plugin.AppleMusic.ExternalIds;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>
/// Supplies artist metadata from the Apple Music catalog.
/// </summary>
public class ArtistMetadataProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>
{
    private readonly IAppleMusicCatalog _catalog;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ArtistMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistMetadataProvider"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public ArtistMetadataProvider(
        IAppleMusicCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<ArtistMetadataProvider> logger)
    {
        _catalog = catalog;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        ArtistInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var artists = await FindAsync(searchInfo, cancellationToken);
        return artists.Select(ToSearchResult);
    }

    /// <inheritdoc />
    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var artists = await FindAsync(info, cancellationToken);
        if (artists.Count == 0)
        {
            _logger.LogDebug("No Apple Music artist for {Name}", info.Name);
            return new MetadataResult<MusicArtist> { HasMetadata = false };
        }

        var artist = artists[0];
        var item = new MusicArtist
        {
            Name = artist.Attributes.Name,
            Overview = artist.Attributes.EditorialNotes?.GetBest(),
        };

        foreach (var genre in artist.Attributes.GenreNames)
        {
            item.AddGenre(genre);
        }

        item.SetProviderId(ProviderKeys.Artist, artist.Id);
        item.SetProviderId(ProviderKeys.Storefront, artist.Storefront);

        return new MetadataResult<MusicArtist> { Item = item, HasMetadata = true };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClient.GetAsync(new Uri(url), cancellationToken);

    private static RemoteSearchResult ToSearchResult(CatalogItem<ArtistAttributes> artist)
    {
        var options = Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();
        var result = new RemoteSearchResult
        {
            Name = artist.Attributes.Name,
            ImageUrl = ArtworkUrl.Resolve(artist.Attributes.Artwork?.Url, options.ArtworkSize),
            SearchProviderName = PluginConstants.Name,
        };

        result.SetProviderId(ProviderKeys.Artist, artist.Id);
        result.SetProviderId(ProviderKeys.Storefront, artist.Storefront);
        return result;
    }

    private async Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> FindAsync(
        ArtistInfo info,
        CancellationToken cancellationToken)
    {
        var id = info.GetProviderId(ProviderKeys.Artist);
        if (!string.IsNullOrEmpty(id))
        {
            var storefront = info.GetProviderId(ProviderKeys.Storefront);
            _logger.LogDebug("Looking up artist by id {Id} ({Storefront})", id, storefront);
            var artist = await _catalog.GetArtistAsync(id, storefront, cancellationToken);
            return artist is null ? [] : [artist];
        }

        _logger.LogDebug("Searching Apple Music artists for {Term}", info.Name);
        return await _catalog.SearchArtistsAsync(info.Name, cancellationToken);
    }
}
