using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.AppleMusic.Providers;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class NfoIdsTests
{
    private const string Album = """
        <?xml version="1.0" encoding="utf-8" standalone="yes"?>
        <album>
          <title>YANKEE</title>
          <applemusicalbumid>1440791809</applemusicalbumid>
          <applemusicstorefrontid>jp</applemusicstorefrontid>
        </album>
        """;

    [Fact]
    public void ParseAlbum_ReadsBothIds()
    {
        var (id, storefront) = NfoIds.ParseAlbum(Album);

        Assert.Equal("1440791809", id);
        Assert.Equal("jp", storefront);
    }

    [Fact]
    public void ParseAlbum_LeavesTheStorefrontNullWhenTheNfoHasNone()
    {
        // An id without a storefront still beats searching; the catalog then
        // walks the configured storefronts in order.
        var (id, storefront) = NfoIds.ParseAlbum("<album><applemusicalbumid>1440791809</applemusicalbumid></album>");

        Assert.Equal("1440791809", id);
        Assert.Null(storefront);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<album><title>YANKEE</title></album>")]
    [InlineData("<album><applemusicalbumid>  </applemusicalbumid></album>")]
    public void ParseAlbum_ReturnsNothingWhenThereIsNoId(string? xml)
        => Assert.Equal((null, null), NfoIds.ParseAlbum(xml));

    [Fact]
    public void ParseAlbum_SurvivesABrokenNfo()
    {
        // Hand-edited nfos are common; one bad file must not fail the scan.
        Assert.Equal((null, null), NfoIds.ParseAlbum("<album><applemusicalbumid>1</album>"));
    }

    [Fact]
    public void FindAlbumInAncestors_ReadsTheNfoBesideTheTrack()
    {
        var files = new Dictionary<string, string> { [P("Artist", "Album", "album.nfo")] = Album };

        var (id, storefront) = NfoIds.FindAlbumInAncestors(P("Artist", "Album", "01.flac"), Read(files));

        Assert.Equal("1440791809", id);
        Assert.Equal("jp", storefront);
    }

    [Fact]
    public void FindAlbumInAncestors_ClimbsOutOfADiscFolder()
    {
        var files = new Dictionary<string, string> { [P("Artist", "Album", "album.nfo")] = Album };

        var (id, _) = NfoIds.FindAlbumInAncestors(P("Artist", "Album", "CD1", "01.flac"), Read(files));

        Assert.Equal("1440791809", id);
    }

    [Fact]
    public void FindAlbumInAncestors_StopsBeforeTheArtistDirectory()
    {
        // An album.nfo two directories up belongs to a different album.
        var files = new Dictionary<string, string> { [P("Artist", "album.nfo")] = Album };

        Assert.Equal((null, null), NfoIds.FindAlbumInAncestors(P("Artist", "Album", "CD1", "01.flac"), Read(files)));
    }

    [Fact]
    public void FindAlbumInAncestors_ReturnsNothingWithoutAnNfo()
        => Assert.Equal((null, null), NfoIds.FindAlbumInAncestors(P("Artist", "Album", "01.flac"), _ => null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FindAlbumInAncestors_HandlesAMissingPath(string? path)
        => Assert.Equal((null, null), NfoIds.FindAlbumInAncestors(path, _ => null));

    private static string P(params string[] parts)
        => Path.Combine([Path.GetTempPath(), "music", .. parts]);

    private static Func<string, string?> Read(IReadOnlyDictionary<string, string> files)
        => path => files.TryGetValue(path, out var content) ? content : null;
}
