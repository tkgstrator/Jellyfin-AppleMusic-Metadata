using System;
using System.Buffers.Binary;
using System.IO;
using Jellyfin.Plugin.AppleMusic.Providers;
using Xunit;
using static Jellyfin.Plugin.AppleMusic.Tests.Mp4Fixture;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public sealed class EmbeddedIdsTests : IDisposable
{
    // The ids of YOASOBI - Idol, as the iTunes Store writes them.
    private const ulong Song = 1679278167;
    private const ulong Album = 1679278166;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "apple-music-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Parse_ReadsTheIdsTheStoreWrites()
    {
        using var stream = Stream(M4a(Song, Album, Japan));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_ReadsAQuickTimeStyleMeta()
    {
        using var stream = Stream(Concat(Ftyp(), Moov(Ilst(Song, Album, Japan), fullBox: false), Mdat()));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_FindsTheMetadataAfterTheAudio()
    {
        using var stream = Stream(Concat(Ftyp(), Mdat(), Moov(Ilst(Song, Album, Japan))));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_SkipsAnAtomWithA64BitSize()
    {
        using var stream = Stream(Concat(Ftyp(), LargeAtom("mdat", new byte[64]), Moov(Ilst(Song, Album, Japan))));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_ReadsAnAtomThatRunsToTheEnd()
    {
        var moov = Moov(Ilst(Song, Album, Japan));
        BinaryPrimitives.WriteUInt32BigEndian(moov, 0);

        using var stream = Stream(Concat(Ftyp(), Mdat(), moov));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_LeavesAnUnknownStorefrontToTheConfiguredOrder()
    {
        // 143444 is the United Kingdom, which the plugin does not query.
        using var stream = Stream(M4a(Song, Album, 143444));

        Assert.Equal(("1679278167", "1679278166", null), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_ReturnsTheIdsThatArePresent()
    {
        using var stream = Stream(M4a(album: Album));

        Assert.Equal((null, "1679278166", null), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_ReturnsNothingForAFileWithoutMetadata()
    {
        using var stream = Stream(Concat(Ftyp(), Mdat()));

        Assert.Equal((null, null, null), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_StopsAtASizeThatOverrunsTheFile()
    {
        var bytes = M4a(Song, Album, Japan);

        // Cut the file short: moov now claims more than there is.
        using var stream = Stream(bytes[..^80]);

        Assert.Equal((null, null, null), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_StopsAtASizeSmallerThanItsHeader()
    {
        var moov = Moov(Ilst(Song, Album, Japan));
        BinaryPrimitives.WriteUInt32BigEndian(moov, 4);

        using var stream = Stream(Concat(Ftyp(), moov));

        Assert.Equal((null, null, null), EmbeddedIds.Parse(stream));
    }

    [Fact]
    public void Parse_IgnoresAValueTooLongForAnInteger()
    {
        var header = new byte[8];
        var ilst = Atom("ilst", Atom("cnID", Atom("data", header, new byte[9])), Integer("plID", Album, 8));

        using var stream = Stream(Concat(Ftyp(), Moov(ilst)));

        Assert.Equal((null, "1679278166", null), EmbeddedIds.Parse(stream));
    }

    [Theory]
    [InlineData(143462UL, "jp")]
    [InlineData(143441UL, "us")]
    [InlineData(143444UL, null)]
    [InlineData(null, null)]
    public void ToStorefront_MapsTheStorefrontsThePluginQueries(ulong? id, string? expected)
        => Assert.Equal(expected, EmbeddedIds.ToStorefront(id));

    [Fact]
    public void Read_ReadsAFileOnDisk()
    {
        var path = Write("01 Idol.m4a", M4a(Song, Album, Japan));

        Assert.Equal(("1679278167", "1679278166", "jp"), EmbeddedIds.Read(path));
    }

    [Fact]
    public void Read_DoesNotOpenFilesThatAreNotMp4()
    {
        var path = Write("01 Idol.flac", M4a(Song, Album, Japan));

        Assert.Equal((null, null, null), EmbeddedIds.Read(path));
    }

    [Fact]
    public void Read_ReturnsNothingForAMissingFile()
    {
        Assert.Equal((null, null, null), EmbeddedIds.Read(Path.Combine(_root, "missing.m4a")));
        Assert.Equal((null, null, null), EmbeddedIds.Read(null));
    }

    [Fact]
    public void FindAlbumInDirectory_TakesTheFirstTrackThatHasAnAlbum()
    {
        Write("cover.jpg", [0xFF, 0xD8]);
        Write("01 Intro.m4a", M4a(Song));
        Write("02 Idol.m4a", M4a(Song, Album, Japan));

        Assert.Equal(("1679278166", "jp"), EmbeddedIds.FindAlbumInDirectory(_root));
    }

    [Fact]
    public void FindAlbumInDirectory_LooksIntoDiscSubdirectories()
    {
        Write(Path.Combine("CD1", "01 Idol.m4a"), M4a(Song, Album, Japan));

        Assert.Equal(("1679278166", "jp"), EmbeddedIds.FindAlbumInDirectory(_root));
    }

    [Fact]
    public void FindAlbumInDirectory_ReturnsNothingWhenNoTrackHasAnAlbum()
    {
        Write("01 Idol.m4a", M4a(Song));

        Assert.Equal((null, null), EmbeddedIds.FindAlbumInDirectory(_root));
        Assert.Equal((null, null), EmbeddedIds.FindAlbumInDirectory(Path.Combine(_root, "missing")));
        Assert.Equal((null, null), EmbeddedIds.FindAlbumInDirectory(null));
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
