using Jellyfin.Plugin.AppleMusic.Catalog;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class CatalogOptionsTests
{
    [Theory]
    [InlineData("jp", "ja-jp")]
    [InlineData("us", "en-us")]
    [InlineData("JP", "ja-jp")]
    public void GetLanguageFor_FollowsTheStorefront(string storefront, string expected)
    {
        var options = new CatalogOptions();

        Assert.Equal(expected, options.GetLanguageFor(storefront));
    }

    [Fact]
    public void GetLanguageFor_HonoursOverrideForEveryStorefront()
    {
        var options = new CatalogOptions { LanguageOverride = "en-gb" };

        Assert.Equal("en-gb", options.GetLanguageFor("jp"));
        Assert.Equal("en-gb", options.GetLanguageFor("us"));
    }

    [Fact]
    public void GetLanguageFor_IgnoresBlankOverride()
    {
        var options = new CatalogOptions { LanguageOverride = "   " };

        Assert.Equal("ja-jp", options.GetLanguageFor("jp"));
    }
}
