using System;
using Jellyfin.Plugin.AppleMusic.Providers;
using MediaBrowser.Controller.Providers;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class ProviderHelpersTests
{
    [Fact]
    public void ParseReleaseDate_ReadsAFullDate()
    {
        var parsed = AlbumMetadataProvider.ParseReleaseDate("2025-09-15");

        Assert.Equal(new DateTime(2025, 9, 15, 0, 0, 0, DateTimeKind.Utc), parsed);
    }

    [Fact]
    public void ParseReleaseDate_AcceptsAYearOnly()
    {
        // Older catalogue entries carry just the year.
        var parsed = AlbumMetadataProvider.ParseReleaseDate("2014");

        Assert.NotNull(parsed);
        Assert.Equal(2014, parsed.Value.Year);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("99")]
    public void ParseReleaseDate_ReturnsNullForUnusableValues(string? value)
    {
        Assert.Null(AlbumMetadataProvider.ParseReleaseDate(value));
    }

    [Fact]
    public void BuildSearchTerm_ForAlbum_CombinesArtistAndTitle()
    {
        var info = new AlbumInfo { Name = "YANKEE", AlbumArtists = ["米津玄師"] };

        Assert.Equal("米津玄師 YANKEE", AlbumMetadataProvider.BuildSearchTerm(info));
    }

    [Fact]
    public void BuildSearchTerm_ForAlbum_SkipsAMissingArtist()
    {
        var info = new AlbumInfo { Name = "YANKEE" };

        Assert.Equal("YANKEE", AlbumMetadataProvider.BuildSearchTerm(info));
    }

    [Fact]
    public void BuildSearchTerm_ForSong_IncludesTheAlbum()
    {
        var info = new SongInfo
        {
            Name = "IRIS OUT",
            Album = "IRIS OUT - Single",
            AlbumArtists = ["米津玄師"],
        };

        Assert.Equal("米津玄師 IRIS OUT - Single IRIS OUT", SongMetadataProvider.BuildSearchTerm(info));
    }

    [Fact]
    public void BuildSearchTerm_ForSong_FallsBackToTheTrackArtist()
    {
        var info = new SongInfo { Name = "IRIS OUT", Artists = ["米津玄師"] };

        Assert.Equal("米津玄師 IRIS OUT", SongMetadataProvider.BuildSearchTerm(info));
    }

    [Fact]
    public void BuildSearchTerm_ForSong_HandlesTitleOnly()
    {
        var info = new SongInfo { Name = "IRIS OUT" };

        Assert.Equal("IRIS OUT", SongMetadataProvider.BuildSearchTerm(info));
    }

    [Theory]
    [InlineData("1991年3月10日", 1991, 3, 10)]
    [InlineData("March 10, 1991", 1991, 3, 10)]
    [InlineData("1991-03-10", 1991, 3, 10)]
    [InlineData("1991年", 1991, 1, 1)]
    [InlineData("Formed in 2013", 2013, 1, 1)]
    public void ParseBornOrFormed_ReadsTheLocalisedForms(string value, int year, int month, int day)
    {
        var parsed = ArtistMetadataProvider.ParseBornOrFormed(value);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc), parsed.Value.Date.ToUniversalTime());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void ParseBornOrFormed_ReturnsNullWhenThereIsNoDate(string? value)
        => Assert.Null(ArtistMetadataProvider.ParseBornOrFormed(value));

    [Fact]
    public void ToPlainText_TurnsBreakTagsIntoNewlines()
        => Assert.Equal("one\ntwo\nthree", ArtistMetadataProvider.ToPlainText("one<br>two<BR />three"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<br>")]
    public void ToPlainText_ReturnsNullWhenNothingIsLeft(string? value)
        => Assert.Null(ArtistMetadataProvider.ToPlainText(value));
}
