using System.Collections.Generic;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// A catalog resource together with the storefront it was found in.
/// </summary>
/// <typeparam name="TAttributes">Attribute payload type.</typeparam>
public class CatalogItem<TAttributes>
    where TAttributes : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogItem{TAttributes}"/> class.
    /// </summary>
    /// <param name="id">Apple Music catalog identifier.</param>
    /// <param name="storefront">Storefront the resource was found in.</param>
    /// <param name="attributes">Attribute payload.</param>
    public CatalogItem(string id, string storefront, TAttributes attributes)
    {
        Id = id;
        Storefront = storefront;
        Attributes = attributes;
    }

    /// <summary>
    /// Gets the Apple Music catalog identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the storefront the resource was found in. Catalog identifiers are
    /// storefront-scoped, so this must be kept alongside the id.
    /// </summary>
    public string Storefront { get; }

    /// <summary>
    /// Gets the attribute payload.
    /// </summary>
    public TAttributes Attributes { get; }

    /// <summary>
    /// Gets the ids of the related artists. Populated by id lookups only.
    /// </summary>
    public IReadOnlyList<string> ArtistIds { get; init; } = [];

    /// <summary>
    /// Gets the ids of the related albums. Populated by song id lookups only.
    /// </summary>
    public IReadOnlyList<string> AlbumIds { get; init; } = [];

    /// <summary>
    /// Gets the album's tracks in order. Populated by album id lookups only.
    /// </summary>
    public IReadOnlyList<CatalogItem<SongAttributes>> Tracks { get; init; } = [];
}
