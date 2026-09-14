using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;
using Jellyfin.Plugin.AppleMusic.Organizer;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class OrganizePlannerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "music");

    [Fact]
    public void Plan_MovesTheAlbumDirectoryAndRenamesTracks()
    {
        var album = Album("Yankee", P("Kenshi Yonezu", "Yankee"), Track("01.flac", id: "t1", disc: 1, track: 1), Track("02.flac", id: "t2", disc: 1, track: 2));
        var catalog = CatalogAlbum("al1", "YANKEE", ("t1", 1, 1, "MAD HEAD LOVE"), ("t2", 1, 2, "ポッピンアパシー"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "米津玄師", "ar1", new OrganizeOptions());

        var target = P("米津玄師-[amid-ar1]", "YANKEE-[amid-al1]");
        Assert.Empty(plan.Skipped);
        Assert.Equal(
            [
                new PlannedMove(MoveKind.Directory, P("Kenshi Yonezu", "Yankee"), target),
                new PlannedMove(MoveKind.File, Path.Combine(target, "01.flac"), Path.Combine(target, "01 MAD HEAD LOVE.flac")),
                new PlannedMove(MoveKind.File, Path.Combine(target, "02.flac"), Path.Combine(target, "02 ポッピンアパシー.flac")),
            ],
            plan.Moves);
        Assert.Equal(P("Kenshi Yonezu"), plan.VacatedDirectory);
        Assert.Equal(P("米津玄師-[amid-ar1]"), plan.TargetArtistDirectory);
    }

    [Fact]
    public void Plan_DoesNothingWhenEverythingIsAlreadyInPlace()
    {
        var dir = P("米津玄師-[amid-ar1]", "YANKEE-[amid-al1]");
        var album = Album("YANKEE", dir, Track("01 MAD HEAD LOVE.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "YANKEE", ("t1", 1, 1, "MAD HEAD LOVE"));

        var plan = new OrganizePlanner(_ => true).Plan(album, catalog, "米津玄師", "ar1", new OrganizeOptions());

        Assert.Empty(plan.Moves);
        Assert.Empty(plan.Skipped);
        Assert.Null(plan.VacatedDirectory);
    }

    [Fact]
    public void Plan_RenamesOnlyTheDirectoryWhenTrackRenamingIsOff()
    {
        var album = Album("Yankee", P("Kenshi Yonezu", "Yankee"), Track("01.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "YANKEE", ("t1", 1, 1, "MAD HEAD LOVE"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "米津玄師", "ar1", new OrganizeOptions { RenameTrackFiles = false });

        var move = Assert.Single(plan.Moves);
        Assert.Equal(MoveKind.Directory, move.Kind);
    }

    [Fact]
    public void Plan_SkipsTheAlbumWhenTheTargetDirectoryExists()
    {
        var album = Album("Yankee", P("Kenshi Yonezu", "Yankee"), Track("01.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "YANKEE", ("t1", 1, 1, "MAD HEAD LOVE"));
        var target = P("米津玄師-[amid-ar1]", "YANKEE-[amid-al1]");

        var plan = new OrganizePlanner(path => path == target).Plan(album, catalog, "米津玄師", "ar1", new OrganizeOptions());

        Assert.Empty(plan.Moves);
        Assert.Contains(plan.Skipped, reason => reason.Contains("already exists", StringComparison.Ordinal));
        Assert.Null(plan.VacatedDirectory);
    }

    [Fact]
    public void Plan_MatchesUntaggedTracksByNumber()
    {
        var album = Album("Yankee", P("Kenshi Yonezu", "Yankee"), Track("track two.flac", id: null, disc: null, track: 2), Track("mystery.flac", id: null, disc: null, track: null));
        var catalog = CatalogAlbum("al1", "YANKEE", ("t1", 1, 1, "MAD HEAD LOVE"), ("t2", 1, 2, "ポッピンアパシー"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "米津玄師", "ar1", new OrganizeOptions());

        var target = P("米津玄師-[amid-ar1]", "YANKEE-[amid-al1]");
        Assert.Contains(new PlannedMove(MoveKind.File, Path.Combine(target, "track two.flac"), Path.Combine(target, "02 ポッピンアパシー.flac")), plan.Moves);
        Assert.Single(plan.Skipped, reason => reason.Contains("mystery.flac", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_PrefixesTheDiscOnMultiDiscAlbumsAndFlattensDiscFolders()
    {
        var dir = P("Artist", "Album");
        var album = Album(
            "Album",
            dir,
            Track(Path.Combine("CD1", "01.flac"), id: "t1", disc: 1, track: 1),
            Track(Path.Combine("CD2", "01.flac"), id: "t2", disc: 2, track: 1));
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"), ("t2", 2, 1, "Two"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        var target = P("Artist-[amid-ar1]", "Album-[amid-al1]");
        Assert.Contains(new PlannedMove(MoveKind.File, Path.Combine(target, "CD1", "01.flac"), Path.Combine(target, "1-01 One.flac")), plan.Moves);
        Assert.Contains(new PlannedMove(MoveKind.File, Path.Combine(target, "CD2", "01.flac"), Path.Combine(target, "2-01 Two.flac")), plan.Moves);
    }

    [Fact]
    public void Plan_RefusesToLetTwoTracksCollide()
    {
        var album = Album("Album", P("Artist", "Album"), Track("a.flac", id: "t1", disc: 1, track: 1), Track("b.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        Assert.Single(plan.Moves, move => move.Kind == MoveKind.File);
        Assert.Single(plan.Skipped, reason => reason.Contains("both become", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_RefusesToOverwriteAnExistingFileWhenTheAlbumStaysPut()
    {
        var dir = P("Artist-[amid-ar1]", "Album-[amid-al1]");
        var album = Album("Album", dir, Track("a.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"));
        var occupied = Path.Combine(dir, "01 One.flac");

        var plan = new OrganizePlanner(path => path == occupied || path == dir).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        Assert.Empty(plan.Moves);
        Assert.Single(plan.Skipped, reason => reason.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_SkipsAlbumsOutsideTheLibraryFolder()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "other", "Album");
        var album = new AlbumSnapshot("Album", elsewhere, Root, [Track(Path.Combine(elsewhere, "a.flac"), id: "t1", disc: 1, track: 1)]);
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        Assert.Empty(plan.Moves);
        Assert.Single(plan.Skipped, reason => reason.Contains("not inside", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_SkipsAlbumsWhoseTracksLiveElsewhere()
    {
        var album = new AlbumSnapshot("Album", P("Artist", "Album"), Root, [Track(P("Artist", "Other", "a.flac"), id: "t1", disc: 1, track: 1)]);
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        Assert.Empty(plan.Moves);
        Assert.Single(plan.Skipped, reason => reason.Contains("not every track", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_SanitisesNamesAndKeepsTheTag()
    {
        var album = Album("Album", P("Artist", "Album"), Track("a.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "Live: A/B?", ("t1", 1, 1, "One"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "AC/DC", "ar1", new OrganizeOptions());

        Assert.Equal(P("AC／DC-[amid-ar1]", "Live： A／B？-[amid-al1]"), plan.Moves[0].To);
    }

    [Fact]
    public void Plan_DoesNotReportAVacatedDirectoryWhenTheAlbumSatInTheRoot()
    {
        var album = Album("Album", P("Album"), Track("a.flac", id: "t1", disc: 1, track: 1));
        var catalog = CatalogAlbum("al1", "Album", ("t1", 1, 1, "One"));

        var plan = new OrganizePlanner(_ => false).Plan(album, catalog, "Artist", "ar1", new OrganizeOptions());

        Assert.NotEmpty(plan.Moves);
        Assert.Null(plan.VacatedDirectory);
    }

    private static string P(params string[] parts)
        => Path.Combine([Root, .. parts]);

    private static AlbumSnapshot Album(string name, string path, params TrackSnapshot[] tracks)
        => new(name, path, Root, tracks.Select(track => track with { Path = Path.Combine(path, track.Path) }).ToList());

    private static TrackSnapshot Track(string path, string? id, int? disc, int? track)
        => new(path, id, disc, track, Path.GetFileNameWithoutExtension(path));

    private static CatalogItem<AlbumAttributes> CatalogAlbum(string id, string name, params (string Id, int Disc, int Track, string Name)[] tracks)
        => new(id, "jp", new AlbumAttributes { Name = name })
        {
            ArtistIds = ["ar1"],
            Tracks = tracks
                .Select(track => new CatalogItem<SongAttributes>(track.Id, "jp", new SongAttributes { Name = track.Name, DiscNumber = track.Disc, TrackNumber = track.Track }))
                .ToList(),
        };
}
