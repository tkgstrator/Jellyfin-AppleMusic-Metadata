using Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class EmbeddedMetadataTests
{
    [Fact]
    public void Parse_PreservesExplicitTagsWithoutInventingAlbumArtist()
    {
        var tags = EmbeddedTagParser.Parse("""
            {"format":{"tags":{"ARTIST":"AC/DC", "album":" Album ", "track":"1/10", "disc":"2/2", "date":"2024-02-29", "genre":"Rock"}}}
            """, new EmbeddedTagOptions());
        Assert.Equal(["AC/DC"], tags.Artists);
        Assert.Empty(tags.AlbumArtists);
        Assert.Equal("Album", tags.Album);
        Assert.Equal(1, tags.Track);
        Assert.Equal(2, tags.Disc);
        Assert.Equal(2024, tags.Year);
        Assert.Equal(["Rock"], tags.Genres);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1/no")]
    [InlineData("2147483648")]
    [InlineData("1/2/3")]
    public void Parse_RejectsInvalidNumbers(string number)
    {
        var json = "{\"format\":{\"tags\":{\"track\":\"" + number + "\"}}}";
        Assert.Null(EmbeddedTagParser.Parse(json, new EmbeddedTagOptions()).Track);
    }

    [Fact]
    public void Parse_RejectsConflictingSourcesPerField()
    {
        var tags = EmbeddedTagParser.Parse("""
            {"format":{"tags":{"track":"1", "artist":"One"}},"streams":[{"codec_type":"audio","tags":{"track":"2", "album":"Album"}}]}
            """, new EmbeddedTagOptions());
        Assert.Null(tags.Track);
        Assert.Equal(["One"], tags.Artists);
        Assert.Equal("Album", tags.Album);
        Assert.Contains("Track", tags.Conflicts);
    }

    [Fact]
    public void Parse_UsesArtistPreferenceAndProtectsWhitelist()
    {
        var options = new EmbeddedTagOptions { PreferNonstandardArtists = true, Delimiters = ["/"], Whitelist = ["AC/DC"] };
        var tags = EmbeddedTagParser.Parse("""
            {"format":{"tags":{"artist":"Wrong","ARTISTS":"AC/DC / Other", "albumartist":"Album Artist"}}}
            """, options);
        Assert.Equal(["AC/DC", "Other"], tags.Artists);
        Assert.Equal(["Album Artist"], tags.AlbumArtists);
    }

    [Theory]
    [InlineData("2023-02-29")]
    [InlineData("2024garbage")]
    [InlineData("0000")]
    public void Parse_RejectsInvalidDates(string date)
    {
        Assert.Null(EmbeddedTagParser.Parse("{\"format\":{\"tags\":{\"date\":\"" + date + "\"}}}", new EmbeddedTagOptions()).Year);
    }

    [Fact]
    public void Parse_DoesNotCombineContradictoryAliases()
    {
        var tags = EmbeddedTagParser.Parse("""
            {"format":{"tags":{"albumartist":"One", "album_artist":"Two"}}}
            """, new EmbeddedTagOptions());
        Assert.Empty(tags.AlbumArtists);
        Assert.Contains("AlbumArtists", tags.Conflicts);
    }

    [Fact]
    public void Parse_KeepsMultipleValuesAndCanonicalUnicode()
    {
        var tags = EmbeddedTagParser.Parse("""
            {"format":{"tags":{"artist":["Beyoncé", "Beyonce\u0301", "Earth, Wind & Fire"],"genre":["Rock","Jazz"]}}}
            """, new EmbeddedTagOptions());
        Assert.Equal(["Beyoncé", "Earth, Wind & Fire"], tags.Artists);
        Assert.Equal(["Rock", "Jazz"], tags.Genres);
    }

    [Theory]
    [InlineData("02", 2)]
    [InlineData("18/20", 18)]
    [InlineData("1", 1)]
    public void Parse_AcceptsPositiveNumbers(string text, int expected)
    {
        Assert.Equal(expected, EmbeddedTagParser.Parse("{\"format\":{\"tags\":{\"track\":\"" + text + "\"}}}", new EmbeddedTagOptions()).Track);
    }

    [Fact]
    public void Parse_IgnoresVideoTagsAndMissingFields()
    {
        var tags = EmbeddedTagParser.Parse("""
            {"streams":[{"codec_type":"video","tags":{"track":"10","artist":"Wrong"}}],"format":{"tags":{"artist":" ","disc":"0"}}}
            """, new EmbeddedTagOptions());
        Assert.Empty(tags.Artists);
        Assert.Empty(tags.AlbumArtists);
        Assert.Null(tags.Track);
        Assert.Null(tags.Disc);
    }
}
