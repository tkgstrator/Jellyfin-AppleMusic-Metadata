using Jellyfin.Plugin.AppleMusic.Organizer;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class FileNamesTests
{
    [Theory]
    [InlineData("フットサル/DANCE", "フットサル／DANCE")]
    [InlineData("What?", "What？")]
    [InlineData("A: B", "A： B")]
    [InlineData("a*b\"c<d>e|f\\g", "a＊b”c＜d＞e｜f＼g")]
    [InlineData("  padded  ", "padded")]
    [InlineData("trailing...", "trailing")]
    [InlineData("tab\there", "tabhere")]
    [InlineData("", "_")]
    [InlineData(null, "_")]
    [InlineData("...", "_")]
    public void Sanitize_ProducesASafePathSegment(string? name, string expected)
        => Assert.Equal(expected, FileNames.Sanitize(name));

    [Theory]
    [InlineData("01 IRIS OUT.flac", null, 1, "IRIS OUT")]
    [InlineData("/music/a/2-03 Title.flac", 2, 3, "Title")]
    [InlineData("01 - Title.mp3", null, 1, "Title")]
    [InlineData("01.mp3", null, 1, "")]
    [InlineData("1.01 Title.mp3", 1, 1, "Title")]
    [InlineData("101 Dalmatians.mp3", null, 101, "Dalmatians")]
    [InlineData("Title.mp3", null, null, "Title")]
    [InlineData("2024 Song.mp3", null, null, "2024 Song")]
    public void ParseTrackFileName_ReadsTheNumbersOffTheName(string path, int? disc, int? track, string title)
        => Assert.Equal((disc, track, title), FileNames.ParseTrackFileName(path));

    [Theory]
    [InlineData(1, 1, "IRIS OUT", ".flac", false, "01 IRIS OUT.flac")]
    [InlineData(1, 12, "Title", ".mp3", false, "12 Title.mp3")]
    [InlineData(2, 3, "Title", ".m4a", true, "2-03 Title.m4a")]
    [InlineData(null, 3, "Title", ".m4a", true, "03 Title.m4a")]
    [InlineData(1, null, "Title", ".m4a", false, "Title.m4a")]
    [InlineData(1, 1, "A/B", ".flac", false, "01 A／B.flac")]
    public void TrackFileName_PrefixesTheNumber(int? disc, int? track, string title, string ext, bool multiDisc, string expected)
        => Assert.Equal(expected, FileNames.TrackFileName(disc, track, title, ext, multiDisc));
}
