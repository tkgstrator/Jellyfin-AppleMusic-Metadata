using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class AppleMusicCatalogTests
{
    [Fact]
    public async Task SearchSongsAsync_StopsAtTheFirstStorefrontWithResults()
    {
        var transport = new FakeTransport(url => url.Contains("/jp/", StringComparison.Ordinal)
            ? Songs(("1837658529", "IRIS OUT"))
            : Songs());
        var catalog = Build(transport);

        var songs = await catalog.SearchSongsAsync("IRIS OUT", CancellationToken.None);

        var song = Assert.Single(songs);
        Assert.Equal("1837658529", song.Id);
        Assert.Equal("jp", song.Storefront);
        Assert.Single(transport.Requests); // us was never asked
    }

    [Fact]
    public async Task SearchSongsAsync_FallsBackToTheNextStorefront()
    {
        var transport = new FakeTransport(url => url.Contains("/us/", StringComparison.Ordinal)
            ? Songs(("1", "Only In The US"))
            : Songs());
        var catalog = Build(transport);

        var songs = await catalog.SearchSongsAsync("Only In The US", CancellationToken.None);

        var song = Assert.Single(songs);
        Assert.Equal("us", song.Storefront);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task SearchSongsAsync_ReturnsEmptyWhenNoStorefrontMatches()
    {
        var transport = new FakeTransport(_ => Songs());
        var catalog = Build(transport);

        var songs = await catalog.SearchSongsAsync("nothing", CancellationToken.None);

        Assert.Empty(songs);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchSongsAsync_SkipsBlankTerms(string term)
    {
        var transport = new FakeTransport(_ => Songs(("1", "never")));
        var catalog = Build(transport);

        var songs = await catalog.SearchSongsAsync(term, CancellationToken.None);

        Assert.Empty(songs);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task SearchSongsAsync_SendsLanguageMatchingEachStorefront()
    {
        var transport = new FakeTransport(_ => Songs());
        var catalog = Build(transport);

        await catalog.SearchSongsAsync("term", CancellationToken.None);

        Assert.Contains("/v1/catalog/jp/search", transport.Requests[0], StringComparison.Ordinal);
        Assert.Contains("l=ja-jp", transport.Requests[0], StringComparison.Ordinal);
        Assert.Contains("/v1/catalog/us/search", transport.Requests[1], StringComparison.Ordinal);
        Assert.Contains("l=en-us", transport.Requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSongsAsync_EscapesTheSearchTerm()
    {
        var transport = new FakeTransport(_ => Songs());
        var catalog = Build(transport);

        await catalog.SearchSongsAsync("米津玄師", CancellationToken.None);

        Assert.Contains("term=%E7%B1%B3%E6%B4%A5%E7%8E%84%E5%B8%AB", transport.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchSongsAsync_RequestsTheConfiguredResultCount()
    {
        var transport = new FakeTransport(_ => Songs());
        var catalog = Build(transport, new CatalogOptions { MaxSearchResults = 7 });

        await catalog.SearchSongsAsync("term", CancellationToken.None);

        Assert.Contains("limit=7", transport.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSongAsync_QueriesOnlyTheGivenStorefront()
    {
        var transport = new FakeTransport(_ => new ResourceList<SongAttributes>
        {
            Data = [new Resource<SongAttributes> { Id = "42", Attributes = new SongAttributes { Name = "IRIS OUT" } }],
        });
        var catalog = Build(transport);

        var song = await catalog.GetSongAsync("42", "us", CancellationToken.None);

        Assert.NotNull(song);
        Assert.Equal("us", song.Storefront);
        Assert.Equal("IRIS OUT", song.Attributes.Name);
        Assert.Single(transport.Requests);
        Assert.Contains("/v1/catalog/us/songs/42", transport.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSongAsync_WalksStorefrontsWhenNoneIsGiven()
    {
        var transport = new FakeTransport(url => url.Contains("/us/", StringComparison.Ordinal)
            ? new ResourceList<SongAttributes>
            {
                Data = [new Resource<SongAttributes> { Id = "42", Attributes = new SongAttributes() }],
            }
            : new ResourceList<SongAttributes>());
        var catalog = Build(transport);

        var song = await catalog.GetSongAsync("42", null, CancellationToken.None);

        Assert.NotNull(song);
        Assert.Equal("us", song.Storefront);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task GetSongAsync_ReturnsNullWhenNothingResolves()
    {
        var transport = new FakeTransport(_ => new ResourceList<SongAttributes>());
        var catalog = Build(transport);

        Assert.Null(await catalog.GetSongAsync("42", null, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAlbumsAsync_ReadsTheAlbumCategory()
    {
        var transport = new FakeTransport(_ => new SearchResponse
        {
            Results = new SearchResults
            {
                Albums = new ResourceList<AlbumAttributes>
                {
                    Data =
                    [
                        new Resource<AlbumAttributes>
                        {
                            Id = "1440791809",
                            Attributes = new AlbumAttributes { Name = "YANKEE", ArtistName = "米津玄師" },
                        }
                    ],
                },
            },
        });
        var catalog = Build(transport);

        var album = Assert.Single(await catalog.SearchAlbumsAsync("YANKEE", CancellationToken.None));

        Assert.Equal("1440791809", album.Id);
        Assert.Equal("YANKEE", album.Attributes.Name);
        Assert.Contains("types=albums", transport.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_IgnoresResourcesWithoutAttributes()
    {
        var transport = new FakeTransport(_ => new SearchResponse
        {
            Results = new SearchResults
            {
                Songs = new ResourceList<SongAttributes>
                {
                    Data = [new Resource<SongAttributes> { Id = "1", Attributes = null }],
                },
            },
        });
        var catalog = Build(transport);

        Assert.Empty(await catalog.SearchSongsAsync("term", CancellationToken.None));
    }

    private static AppleMusicCatalog Build(ICatalogTransport transport, CatalogOptions? options = null)
        => new(transport, () => options ?? new CatalogOptions(), NullLogger<AppleMusicCatalog>.Instance);

    private static SearchResponse Songs(params (string Id, string Name)[] songs)
    {
        var data = new List<Resource<SongAttributes>>();
        foreach (var (id, name) in songs)
        {
            data.Add(new Resource<SongAttributes> { Id = id, Attributes = new SongAttributes { Name = name } });
        }

        return new SearchResponse
        {
            Results = new SearchResults { Songs = new ResourceList<SongAttributes> { Data = data } },
        };
    }

    private sealed class FakeTransport : ICatalogTransport
    {
        private readonly Func<string, object?> _responder;

        public FakeTransport(Func<string, object?> responder)
        {
            _responder = responder;
        }

        public List<string> Requests { get; } = [];

        public Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
        {
            Requests.Add(relativeUrl);
            var payload = _responder(relativeUrl);
            return Task.FromResult(payload is null
                ? null
                : JsonSerializer.Serialize(payload, CatalogJson.Options));
        }
    }
}
