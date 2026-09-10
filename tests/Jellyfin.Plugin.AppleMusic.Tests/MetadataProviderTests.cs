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
using Jellyfin.Plugin.AppleMusic.Providers;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class MetadataProviderTests
{
    [Fact]
    public async Task AlbumProvider_MapsEveryFieldOntoTheJellyfinItem()
    {
        var catalog = new FakeCatalog
        {
            Albums =
            [
                new CatalogItem<AlbumAttributes>("1440791809", "jp", new AlbumAttributes
                {
                    Name = "YANKEE",
                    ArtistName = "米津玄師",
                    ReleaseDate = "2014-04-23",
                    GenreNames = ["J-Pop", "ミュージック"],
                    EditorialNotes = new EditorialNotes { Standard = "ボカロP ハチ として…" },
                })
            ],
        };
        var provider = new AlbumMetadataProvider(catalog, new StubHttpClientFactory(), NullLogger<AlbumMetadataProvider>.Instance);

        var result = await provider.GetMetadata(new AlbumInfo { Name = "YANKEE" }, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("YANKEE", result.Item.Name);
        Assert.Equal("ボカロP ハチ として…", result.Item.Overview);
        Assert.Equal(2014, result.Item.ProductionYear);
        Assert.Equal(["米津玄師"], result.Item.AlbumArtists);
        Assert.Equal(["J-Pop", "ミュージック"], result.Item.Genres);
        Assert.Equal("1440791809", result.Item.GetProviderId(ProviderKeys.Album));
        Assert.Equal("jp", result.Item.GetProviderId(ProviderKeys.Storefront));
    }

    [Fact]
    public async Task AlbumProvider_ReportsNoMetadataWhenNothingMatches()
    {
        var provider = new AlbumMetadataProvider(new FakeCatalog(), new StubHttpClientFactory(), NullLogger<AlbumMetadataProvider>.Instance);

        var result = await provider.GetMetadata(new AlbumInfo { Name = "unknown" }, CancellationToken.None);

        Assert.False(result.HasMetadata);
    }

    [Fact]
    public async Task AlbumProvider_PrefersTheStoredIdOverSearching()
    {
        var catalog = new FakeCatalog
        {
            Albums = [new CatalogItem<AlbumAttributes>("1440791809", "jp", new AlbumAttributes { Name = "YANKEE" })],
        };
        var provider = new AlbumMetadataProvider(catalog, new StubHttpClientFactory(), NullLogger<AlbumMetadataProvider>.Instance);

        var info = new AlbumInfo { Name = "YANKEE" };
        info.SetProviderId(ProviderKeys.Album, "1440791809");
        info.SetProviderId(ProviderKeys.Storefront, "jp");

        await provider.GetMetadata(info, CancellationToken.None);

        Assert.Equal([("1440791809", "jp")], catalog.AlbumLookups);
        Assert.Empty(catalog.Searches);
    }

    [Fact]
    public async Task SongProvider_MapsTrackFieldsAndComposer()
    {
        var catalog = new FakeCatalog
        {
            Songs =
            [
                new CatalogItem<SongAttributes>("1837658529", "jp", new SongAttributes
                {
                    Name = "IRIS OUT",
                    ArtistName = "米津玄師",
                    AlbumArtistName = "米津玄師",
                    AlbumName = "IRIS OUT - Single",
                    ComposerName = "米津玄師",
                    TrackNumber = 1,
                    DiscNumber = 1,
                    DurationInMillis = 151573,
                    ReleaseDate = "2025-09-15",
                    GenreNames = ["J-Pop"],
                    Isrc = "JPU902502821",
                })
            ],
        };
        var provider = new SongMetadataProvider(catalog, new StubHttpClientFactory(), NullLogger<SongMetadataProvider>.Instance);

        var result = await provider.GetMetadata(new SongInfo { Name = "IRIS OUT" }, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("IRIS OUT", result.Item.Name);
        Assert.Equal("IRIS OUT - Single", result.Item.Album);
        Assert.Equal(1, result.Item.IndexNumber);
        Assert.Equal(1, result.Item.ParentIndexNumber);
        Assert.Equal(2025, result.Item.ProductionYear);
        Assert.Equal(TimeSpan.FromMilliseconds(151573).Ticks, result.Item.RunTimeTicks);
        Assert.Equal(["米津玄師"], result.Item.Artists);

        var composer = Assert.Single(result.People);
        Assert.Equal("米津玄師", composer.Name);
        Assert.Equal(PersonKind.Composer, composer.Type);
    }

    [Fact]
    public async Task ArtistProvider_MapsNameOverviewAndGenres()
    {
        var catalog = new FakeCatalog
        {
            Artists =
            [
                new CatalogItem<ArtistAttributes>("530814268", "jp", new ArtistAttributes
                {
                    Name = "米津玄師",
                    GenreNames = ["J-Pop"],
                    EditorialNotes = new EditorialNotes { Short = "短い紹介" },
                })
            ],
        };
        var provider = new ArtistMetadataProvider(catalog, new StubHttpClientFactory(), NullLogger<ArtistMetadataProvider>.Instance);

        var result = await provider.GetMetadata(new ArtistInfo { Name = "米津玄師" }, CancellationToken.None);

        Assert.True(result.HasMetadata);
        Assert.Equal("米津玄師", result.Item.Name);
        Assert.Equal("短い紹介", result.Item.Overview); // falls back to the short note
        Assert.Equal(["J-Pop"], result.Item.Genres);
        Assert.Equal("530814268", result.Item.GetProviderId(ProviderKeys.Artist));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeCatalog : IAppleMusicCatalog
    {
        public IReadOnlyList<CatalogItem<SongAttributes>> Songs { get; init; } = [];

        public IReadOnlyList<CatalogItem<AlbumAttributes>> Albums { get; init; } = [];

        public IReadOnlyList<CatalogItem<ArtistAttributes>> Artists { get; init; } = [];

        public List<string> Searches { get; } = [];

        public List<(string Id, string? Storefront)> AlbumLookups { get; } = [];

        public Task<IReadOnlyList<CatalogItem<SongAttributes>>> SearchSongsAsync(string term, CancellationToken cancellationToken)
        {
            Searches.Add(term);
            return Task.FromResult(Songs);
        }

        public Task<IReadOnlyList<CatalogItem<AlbumAttributes>>> SearchAlbumsAsync(string term, CancellationToken cancellationToken)
        {
            Searches.Add(term);
            return Task.FromResult(Albums);
        }

        public Task<IReadOnlyList<CatalogItem<ArtistAttributes>>> SearchArtistsAsync(string term, CancellationToken cancellationToken)
        {
            Searches.Add(term);
            return Task.FromResult(Artists);
        }

        public Task<CatalogItem<SongAttributes>?> GetSongAsync(string id, string? storefront, CancellationToken cancellationToken)
            => Task.FromResult(Songs.FirstOrDefault(s => s.Id == id));

        public Task<CatalogItem<AlbumAttributes>?> GetAlbumAsync(string id, string? storefront, CancellationToken cancellationToken)
        {
            AlbumLookups.Add((id, storefront));
            return Task.FromResult(Albums.FirstOrDefault(a => a.Id == id));
        }

        public Task<CatalogItem<ArtistAttributes>?> GetArtistAsync(string id, string? storefront, CancellationToken cancellationToken)
            => Task.FromResult(Artists.FirstOrDefault(a => a.Id == id));
    }
}
