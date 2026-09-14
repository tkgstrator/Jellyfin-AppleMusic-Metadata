using System.IO;
using System.Linq;
using Jellyfin.Plugin.AppleMusic.Organizer;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class OrganizeReportTests
{
    [Fact]
    public void GroupByArtist_PutsEveryAlbumOfAnArtistUnderOneHeading()
    {
        var artists = OrganizeReport.GroupByArtist(
        [
            Plan("YANKEE", "米津玄師", "ar1", "al1", Move(MoveKind.Directory), Move(MoveKind.File), Move(MoveKind.File)),
            Plan("Bremen", "米津玄師", "ar1", "al2", Move(MoveKind.Directory), Move(MoveKind.File)),
        ]);

        var artist = Assert.Single(artists);
        Assert.Equal("米津玄師", artist.Artist);
        Assert.Equal("ar1", artist.ArtistId);
        Assert.Equal(2, artist.AlbumCount);
        Assert.Equal(2, artist.MovedAlbums);
        Assert.Equal(3, artist.TrackRenames);
        Assert.Equal(0, artist.SkippedCount);
    }

    [Fact]
    public void GroupByArtist_TellsTheSameNameApartByItsId()
    {
        var artists = OrganizeReport.GroupByArtist([Plan("A", "Nirvana", "ar1", "al1"), Plan("B", "Nirvana", "ar2", "al2")]);

        Assert.Equal(2, artists.Count);
        Assert.Equal(["ar1", "ar2"], artists.Select(artist => artist.ArtistId));
    }

    [Fact]
    public void GroupByArtist_OrdersArtistsAndTheirAlbumsByName()
    {
        var artists = OrganizeReport.GroupByArtist(
        [
            Plan("Yankee", "Zombie", "ar2", "al3"),
            Plan("bremen", "adele", "ar1", "al2"),
            Plan("Adele", "adele", "ar1", "al1"),
        ]);

        Assert.Equal(["adele", "Zombie"], artists.Select(artist => artist.Artist));
        Assert.Equal(["Adele", "bremen"], artists[0].Albums.Select(album => album.Album));
    }

    [Fact]
    public void GroupByArtist_ReportsUnresolvedAlbumsLastRatherThanDroppingThem()
    {
        // An album the catalog could not answer for carries no artist at all.
        var unresolved = new AlbumPlan("Mystery", [], ["Mystery: could not be fetched"], null, string.Empty);

        var artists = OrganizeReport.GroupByArtist([unresolved, Plan("YANKEE", "米津玄師", "ar1", "al1")]);

        Assert.Equal(2, artists.Count);
        Assert.Equal("米津玄師", artists[0].Artist);
        Assert.Equal(string.Empty, artists[1].Artist);
        Assert.Equal(1, artists[1].SkippedCount);
    }

    [Fact]
    public void GroupByArtist_LeavesTheTargetNameEmptyForAnAlbumThatStaysPut()
    {
        var staysPut = Plan("YANKEE", "米津玄師", "ar1", "al1") with { TargetAlbumDirectory = string.Empty };

        var album = Assert.Single(OrganizeReport.GroupByArtist([staysPut])[0].Albums);

        Assert.Equal(string.Empty, album.TargetName);
        Assert.False(album.DirectoryMoves);
    }

    [Fact]
    public void GroupByArtist_ReportsTheTargetAsADirectoryNameNotAPath()
    {
        var album = Assert.Single(OrganizeReport.GroupByArtist([Plan("YANKEE", "米津玄師", "ar1", "al1")])[0].Albums);

        Assert.Equal("YANKEE-[amid-al1]", album.TargetName);
    }

    [Fact]
    public void GroupByArtist_CountsSidecarsSeparatelyFromTrackRenames()
    {
        // Lyrics travel with their track; counting them as tracks would make
        // the report claim more renames than there are songs.
        var plan = Plan("YANKEE", "米津玄師", "ar1", "al1", Move(MoveKind.File), Move(MoveKind.Sidecar));

        var album = Assert.Single(OrganizeReport.GroupByArtist([plan])[0].Albums);

        Assert.Equal(1, album.TrackRenames);
    }

    private static PlannedMove Move(MoveKind kind)
        => new(kind, "from", "to");

    private static AlbumPlan Plan(string album, string artist, string artistId, string albumId, params PlannedMove[] moves)
        => new(album, moves, [], null, Path.Combine("/music", $"{artist}-[amid-{artistId}]"))
        {
            Artist = artist,
            ArtistId = artistId,
            AlbumId = albumId,
            TargetAlbumDirectory = Path.Combine("/music", $"{artist}-[amid-{artistId}]", $"{album}-[amid-{albumId}]"),
        };
}
