using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
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
/// Supplies track metadata from the Apple Music catalog.
/// </summary>
public class SongMetadataProvider : IRemoteMetadataProvider<Audio, SongInfo>
{
    private readonly IAppleMusicCatalog _catalog;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SongMetadataProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SongMetadataProvider"/> class.
    /// </summary>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public SongMetadataProvider(
        IAppleMusicCatalog catalog,
        IHttpClientFactory httpClientFactory,
        ILogger<SongMetadataProvider> logger)
    {
        _catalog = catalog;
        _httpClient = httpClientFactory.CreateClient(NamedClient.Default);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        SongInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var songs = await FindAsync(searchInfo, cancellationToken);
        return songs.Select(ToSearchResult);
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
    {
        var songs = await FindAsync(info, cancellationToken);
        if (songs.Count == 0)
        {
            _logger.LogDebug("No Apple Music song for {Name}", info.Name);
            return new MetadataResult<Audio> { HasMetadata = false };
        }

        var song = songs[0];
        var attributes = song.Attributes;

        var item = new Audio
        {
            Name = attributes.Name,
            Album = attributes.AlbumName,
            IndexNumber = attributes.TrackNumber,
            ParentIndexNumber = attributes.DiscNumber,
        };

        if (!string.IsNullOrWhiteSpace(attributes.ArtistName))
        {
            item.Artists = [attributes.ArtistName];
        }

        if (!string.IsNullOrWhiteSpace(attributes.AlbumArtistName))
        {
            item.AlbumArtists = [attributes.AlbumArtistName];
        }

        var released = AlbumMetadataProvider.ParseReleaseDate(attributes.ReleaseDate);
        if (released is not null)
        {
            item.PremiereDate = released;
            item.ProductionYear = released.Value.Year;
        }

        if (attributes.DurationInMillis is > 0)
        {
            item.RunTimeTicks = TimeSpan.FromMilliseconds(attributes.DurationInMillis.Value).Ticks;
        }

        foreach (var genre in attributes.GenreNames)
        {
            item.AddGenre(genre);
        }

        item.SetProviderId(ProviderKeys.Song, song.Id);
        item.SetProviderId(ProviderKeys.Storefront, song.Storefront);

        // Known from the song's relationships on an id lookup, or from the
        // tagged album directory the track was resolved through.
        var albumId = song.AlbumIds.Count > 0 ? song.AlbumIds[0] : FolderTag.FindInAncestors(info.Path);
        if (albumId is not null)
        {
            item.SetProviderId(ProviderKeys.Album, albumId);
        }

        var result = new MetadataResult<Audio> { Item = item, HasMetadata = true };

        if (!string.IsNullOrWhiteSpace(attributes.ComposerName))
        {
            result.AddPerson(new PersonInfo
            {
                Name = attributes.ComposerName,
                Type = PersonKind.Composer,
            });
        }

        return result;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _httpClient.GetAsync(new Uri(url), cancellationToken);

    /// <summary>
    /// Builds the search term for a track lookup. Includes the album when
    /// known, since track titles alone are frequently ambiguous.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <returns>Search term.</returns>
    internal static string BuildSearchTerm(SongInfo info)
    {
        var artist = info.AlbumArtists.Count > 0
            ? info.AlbumArtists[0]
            : info.Artists.Count > 0 ? info.Artists[0] : string.Empty;

        var parts = new[] { artist, info.Album, info.Name };
        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static RemoteSearchResult ToSearchResult(CatalogItem<SongAttributes> song)
    {
        var options = Plugin.Instance?.Configuration.ToCatalogOptions() ?? new CatalogOptions();
        var result = new RemoteSearchResult
        {
            Name = song.Attributes.Name,
            ImageUrl = ArtworkUrl.Resolve(song.Attributes.Artwork?.Url, options.ArtworkSize),
            SearchProviderName = PluginConstants.Name,
            ProductionYear = AlbumMetadataProvider.ParseReleaseDate(song.Attributes.ReleaseDate)?.Year,
        };

        result.SetProviderId(ProviderKeys.Song, song.Id);
        result.SetProviderId(ProviderKeys.Storefront, song.Storefront);
        return result;
    }

    private async Task<IReadOnlyList<CatalogItem<SongAttributes>>> FindAsync(
        SongInfo info,
        CancellationToken cancellationToken)
    {
        var id = info.GetProviderId(ProviderKeys.Song);
        if (!string.IsNullOrEmpty(id))
        {
            var storefront = info.GetProviderId(ProviderKeys.Storefront);
            _logger.LogDebug("Looking up song by id {Id} ({Storefront})", id, storefront);
            var song = await _catalog.GetSongAsync(id, storefront, cancellationToken);
            return song is null ? [] : [song];
        }

        var albumId = FolderTag.FindInAncestors(info.Path);
        if (albumId is not null)
        {
            _logger.LogDebug("Resolving the song through the album tagged on its directory: {Id}", albumId);
            var album = await _catalog.GetAlbumAsync(albumId, null, cancellationToken);
            var track = album is null ? null : MatchTrack(album.Tracks, info);
            if (track is not null)
            {
                return [track];
            }
        }

        var term = BuildSearchTerm(info);
        _logger.LogDebug("Searching Apple Music songs for {Term}", term);
        return await _catalog.SearchSongsAsync(term, cancellationToken);
    }

    /// <summary>
    /// Picks the album track a file corresponds to, by disc and track number
    /// first and by title otherwise.
    /// </summary>
    /// <param name="tracks">The album's tracks.</param>
    /// <param name="info">Lookup info of the file.</param>
    /// <returns>The matching track, or null.</returns>
    internal static CatalogItem<SongAttributes>? MatchTrack(IReadOnlyList<CatalogItem<SongAttributes>> tracks, SongInfo info)
    {
        // On the first scan the tags have not been probed yet, so fall back to
        // whatever the file name says.
        var (fileDisc, fileTrack, fileTitle) = FileNames.ParseTrackFileName(info.Path ?? string.Empty);
        var trackNumber = info.IndexNumber is > 0 ? info.IndexNumber : fileTrack;
        var discNumber = info.IndexNumber is > 0 ? info.ParentIndexNumber : fileDisc;

        if (trackNumber is > 0)
        {
            var byNumber = tracks
                .Where(track => track.Attributes.TrackNumber == trackNumber
                    && (discNumber is null || track.Attributes.DiscNumber is null || track.Attributes.DiscNumber == discNumber))
                .ToList();
            if (byNumber.Count == 1)
            {
                return byNumber[0];
            }
        }

        foreach (var title in new[] { info.Name, fileTitle })
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var byName = tracks
                .Where(track => string.Equals(FileNames.Sanitize(track.Attributes.Name), FileNames.Sanitize(title), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

        return null;
    }
}
