using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Default <see cref="IAppleMusicCatalog"/>: queries each configured storefront
/// in order and returns the first non-empty result.
/// </summary>
public class AppleMusicCatalog : IAppleMusicCatalog
{
    private const string SongsType = "songs";
    private const string AlbumsType = "albums";
    private const string ArtistsType = "artists";

    private readonly ICatalogTransport _transport;
    private readonly Func<CatalogOptions> _options;
    private readonly ILogger<AppleMusicCatalog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicCatalog"/> class.
    /// </summary>
    /// <param name="transport">Transport carrying the requests.</param>
    /// <param name="options">
    /// Supplies the current options. A delegate rather than a value, because the
    /// user can change the settings while the server is running.
    /// </param>
    /// <param name="logger">Logger.</param>
    public AppleMusicCatalog(
        ICatalogTransport transport,
        Func<CatalogOptions> options,
        ILogger<AppleMusicCatalog> logger)
    {
        _transport = transport;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogItem<SongAttributes>>> SearchSongsAsync(string term, CancellationToken cancellationToken)
        => SearchAsync(term, SongsType, results => results.Songs, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogItem<AlbumAttributes>>> SearchAlbumsAsync(string term, CancellationToken cancellationToken)
        => SearchAsync(term, AlbumsType, results => results.Albums, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, CancellationToken cancellationToken)
        => SearchAsync(term, ArtistsType, results => results.Artists, cancellationToken);

    /// <inheritdoc />
    public Task<CatalogItem<SongAttributes>?> GetSongAsync(string id, string? storefront, CancellationToken cancellationToken)
        => GetByIdAsync<SongAttributes>(id, SongsType, storefront, cancellationToken);

    /// <inheritdoc />
    public Task<CatalogItem<AlbumAttributes>?> GetAlbumAsync(string id, string? storefront, CancellationToken cancellationToken)
        => GetByIdAsync<AlbumAttributes>(id, AlbumsType, storefront, cancellationToken);

    /// <inheritdoc />
    public Task<CatalogItem<ArtistAttributes>?> GetArtistAsync(string id, string? storefront, CancellationToken cancellationToken)
        => GetByIdAsync<ArtistAttributes>(id, ArtistsType, storefront, cancellationToken);

    private static IReadOnlyList<CatalogItem<TAttributes>> ToItems<TAttributes>(
        ResourceList<TAttributes>? list,
        string storefront)
        where TAttributes : class
    {
        if (list is null)
        {
            return [];
        }

        return list.Data
            .Where(resource => resource.Attributes is not null && !string.IsNullOrEmpty(resource.Id))
            .Select(resource => new CatalogItem<TAttributes>(resource.Id, storefront, resource.Attributes!))
            .ToList();
    }

    private async Task<IReadOnlyList<CatalogItem<TAttributes>>> SearchAsync<TAttributes>(
        string term,
        string type,
        Func<SearchResults, ResourceList<TAttributes>?> select,
        CancellationToken cancellationToken)
        where TAttributes : class
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            _logger.LogDebug("Empty search term for {Type}, skipping", type);
            return [];
        }

        var options = _options();
        foreach (var storefront in options.Storefronts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = string.Format(
                CultureInfo.InvariantCulture,
                "/v1/catalog/{0}/search?term={1}&types={2}&limit={3}&l={4}",
                Uri.EscapeDataString(storefront),
                Uri.EscapeDataString(term),
                type,
                options.MaxSearchResults,
                Uri.EscapeDataString(options.GetLanguageFor(storefront)));

            var response = await _transport.GetAsync<SearchResponse>(url, cancellationToken);
            var items = response?.Results is null ? [] : ToItems(select(response.Results), storefront);
            if (items.Count > 0)
            {
                _logger.LogInformation(
                    "Found {Count} {Type} for {Term} in storefront {Storefront}",
                    items.Count,
                    type,
                    term,
                    storefront);
                return items;
            }

            _logger.LogDebug("No {Type} for {Term} in storefront {Storefront}", type, term, storefront);
        }

        return [];
    }

    private async Task<CatalogItem<TAttributes>?> GetByIdAsync<TAttributes>(
        string id,
        string type,
        string? storefront,
        CancellationToken cancellationToken)
        where TAttributes : class
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var options = _options();

        // Catalog identifiers are storefront-scoped. When the caller knows which
        // storefront the id came from, ask only that one.
        var storefronts = string.IsNullOrWhiteSpace(storefront)
            ? options.Storefronts
            : [storefront];

        foreach (var current in storefronts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = string.Format(
                CultureInfo.InvariantCulture,
                "/v1/catalog/{0}/{1}/{2}?l={3}",
                Uri.EscapeDataString(current),
                type,
                Uri.EscapeDataString(id),
                Uri.EscapeDataString(options.GetLanguageFor(current)));

            var response = await _transport.GetAsync<ResourceList<TAttributes>>(url, cancellationToken);
            var items = ToItems(response, current);
            if (items.Count > 0)
            {
                _logger.LogDebug("Resolved {Type} {Id} in storefront {Storefront}", type, id, current);
                return items[0];
            }
        }

        _logger.LogDebug("Could not resolve {Type} {Id}", type, id);
        return null;
    }
}
