using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

/// <summary>
/// Exercises the real Apple Music endpoint end to end: token scrape, Origin
/// header, storefront selection, Japanese localisation and artwork resolution.
/// </summary>
/// <remarks>
/// Skipped by default so CI never depends on the network or adds load on Apple.
/// These are the only checks that catch Apple changing its web player bundle,
/// so run them by hand — drop the Skip argument — whenever the token scrape is
/// suspect, and before cutting a release:
/// <code>
/// dotnet test -f net10.0 --filter "FullyQualifiedName~LiveCatalogTests"
/// </code>
/// </remarks>
public class LiveCatalogTests
{
    private const string LiveSkip =
        "Hits the live Apple Music API; remove the Skip argument to run it by hand.";

    [Fact(Skip = LiveSkip)]
    public async Task SearchSongs_ReturnsJapaneseMetadataFromTheJpStorefront()
    {
        var catalog = BuildCatalog(2);

        var songs = await catalog.SearchSongsAsync("米津玄師 IRIS OUT", CancellationToken.None);

        Assert.NotEmpty(songs);
        var song = songs[0];

        // The jp storefront is tried first and must satisfy a Japanese query.
        Assert.Equal("jp", song.Storefront);

        // Localised text, not the romanised US form.
        Assert.Equal("米津玄師", song.Attributes.ArtistName);
        Assert.False(string.IsNullOrWhiteSpace(song.Attributes.Name));

        // Fields the iTunes Search API cannot provide.
        Assert.False(string.IsNullOrWhiteSpace(song.Attributes.Isrc));
        Assert.NotEmpty(song.Attributes.GenreNames);

        var artwork = ArtworkUrl.Resolve(song.Attributes.Artwork?.Url, 1400);
        Assert.NotNull(artwork);
        Assert.StartsWith("https://", artwork, System.StringComparison.Ordinal);
        Assert.Contains("1400x1400", artwork, System.StringComparison.Ordinal);
        Assert.DoesNotContain("{w}", artwork, System.StringComparison.Ordinal);

        // The id round-trips through the storefront it was found in.
        var byId = await catalog.GetSongAsync(song.Id, song.Storefront, CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal(song.Attributes.Name, byId.Attributes.Name);
    }

    [Fact(Skip = LiveSkip)]
    public async Task SearchArtists_ReturnsArtistArtwork()
    {
        var catalog = BuildCatalog(1);

        var artists = await catalog.SearchArtistsAsync("米津玄師", CancellationToken.None);

        Assert.NotEmpty(artists);
        var artist = artists[0];

        Assert.Equal("jp", artist.Storefront);
        Assert.Equal("米津玄師", artist.Attributes.Name);

        // Artist images are the main thing the iTunes Search API cannot give us.
        Assert.NotNull(ArtworkUrl.Resolve(artist.Attributes.Artwork?.Url, 1400));
    }

    [Fact(Skip = LiveSkip)]
    public async Task SearchAlbums_ReturnsJapaneseEditorialNotes()
    {
        var catalog = BuildCatalog(5);

        var albums = await catalog.SearchAlbumsAsync("米津玄師 YANKEE", CancellationToken.None);

        Assert.NotEmpty(albums);
        var album = albums[0];

        Assert.Equal("jp", album.Storefront);
        Assert.False(string.IsNullOrWhiteSpace(album.Attributes.Name));
        Assert.NotNull(ArtworkUrl.Resolve(album.Attributes.Artwork?.Url, 1400));
    }

    [Fact(Skip = LiveSkip)]
    public async Task SearchSongs_UsesTheUsStorefrontWhenConfigured()
    {
        var catalog = BuildCatalog(1, new CatalogOptions
        {
            Storefronts = [CatalogOptions.UnitedStates],
            MaxSearchResults = 1,
        });

        var songs = await catalog.SearchSongsAsync("米津玄師 IRIS OUT", CancellationToken.None);

        Assert.NotEmpty(songs);
        var song = songs[0];

        Assert.Equal("us", song.Storefront);

        // Same query, same track — but the US storefront romanises the name.
        Assert.Equal("Kenshi Yonezu", song.Attributes.ArtistName);
    }

    private static AppleMusicCatalog BuildCatalog(int limit, CatalogOptions? options = null)
    {
        var http = new HttpClient();
        var tokens = new WebPlayTokenProvider(http, NullLogger<WebPlayTokenProvider>.Instance);
        var transport = new WebPlayTransport(http, tokens, NullLogger<WebPlayTransport>.Instance);
        return new AppleMusicCatalog(
            transport,
            () => options ?? new CatalogOptions { MaxSearchResults = limit },
            NullLogger<AppleMusicCatalog>.Instance);
    }
}
