using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Supplies album metadata from the Apple Music catalog.
/// </summary>
public class AlbumMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>
{
    private readonly IAppleMusicCatalog _catalog;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlbumMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumMetadataProvider"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AlbumMetadataProvider(
        IAppleMusicCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<AlbumMetadataProvider> logger)
    {
        _catalog = catalog;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        AlbumInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var albums = await FindAsync(searchInfo, cancellationToken);
        return albums.Select(album => ToSearchResult(album, searchInfo));
    }

    /// <inheritdoc />
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        var albums = await FindAsync(info, cancellationToken);
        if (albums.Count == 0)
        {
            _logger.LogDebug("No Apple Music album for {Name}", info.Name);
            return new MetadataResult<MusicAlbum> { HasMetadata = false };
        }

        var album = albums[0];
        var attributes = album.Attributes;
        var artists = string.IsNullOrWhiteSpace(attributes.ArtistName)
            ? new List<string>()
            : [attributes.ArtistName];

        var item = new MusicAlbum
        {
            Name = attributes.Name,
            Overview = attributes.EditorialNotes?.GetBest(),
            AlbumArtists = artists,
            Artists = artists,
        };

        var released = ParseReleaseDate(attributes.ReleaseDate);
        if (released is not null)
        {
            item.PremiereDate = released;
            item.ProductionYear = released.Value.Year;
        }

        foreach (var genre in attributes.GenreNames)
        {
            item.AddGenre(genre);
        }

        item.SetProviderId(ProviderKeys.Album, album.Id);
        item.SetProviderId(ProviderKeys.Storefront, album.Storefront);

        return new MetadataResult<MusicAlbum> { Item = item, HasMetadata = true };
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClient.GetAsync(new Uri(url), cancellationToken);

    /// <summary>
    /// Parses an Apple Music release date, which may be a full date or just a year.
    /// </summary>
    /// <param name="value">Raw value.</param>
    /// <returns>The parsed date, or null.</returns>
    internal static DateTime? ParseReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is > 1000 and < 3000)
        {
            return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>
    /// Builds the search term for an album lookup.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <returns>Search term.</returns>
    internal static string BuildSearchTerm(AlbumInfo info)
    {
        var artist = info.AlbumArtists.Count > 0 ? info.AlbumArtists[0] : string.Empty;
        return string.Join(' ', new[] { artist, info.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static RemoteSearchResult ToSearchResult(CatalogItem<AlbumAttributes> album, AlbumInfo searchInfo)
    {
        var options = Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();
        var result = new RemoteSearchResult
        {
            Name = album.Attributes.Name,
            ImageUrl = ArtworkUrl.Resolve(album.Attributes.Artwork?.Url, options.ArtworkSize),
            SearchProviderName = PluginConstants.Name,
            ProductionYear = ParseReleaseDate(album.Attributes.ReleaseDate)?.Year,
        };

        result.SetProviderId(ProviderKeys.Album, album.Id);
        result.SetProviderId(ProviderKeys.Storefront, album.Storefront);
        return result;
    }

    private async Task<IReadOnlyList<CatalogItem<AlbumAttributes>>> FindAsync(
        AlbumInfo info,
        CancellationToken cancellationToken)
    {
        var id = info.GetProviderId(ProviderKeys.Album);
        if (!string.IsNullOrEmpty(id))
        {
            var storefront = info.GetProviderId(ProviderKeys.Storefront);
            _logger.LogDebug("Looking up album by id {Id} ({Storefront})", id, storefront);
            var album = await _catalog.GetAlbumAsync(id, storefront, cancellationToken);
            return album is null ? [] : [album];
        }

        var term = BuildSearchTerm(info);
        _logger.LogDebug("Searching Apple Music albums for {Term}", term);
        return await _catalog.SearchAlbumsAsync(term, cancellationToken);
    }
}
