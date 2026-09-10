using Jellyfin.Plugin.AppleMusic.Configuration;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class PluginConfigurationTests
{
    [Theory]
    [InlineData(StorefrontPriority.JapanThenUnitedStates, new[] { "jp", "us" })]
    [InlineData(StorefrontPriority.UnitedStatesThenJapan, new[] { "us", "jp" })]
    [InlineData(StorefrontPriority.JapanOnly, new[] { "jp" })]
    [InlineData(StorefrontPriority.UnitedStatesOnly, new[] { "us" })]
    public void GetStorefrontOrder_ReturnsExpectedOrder(StorefrontPriority priority, string[] expected)
    {
        var config = new PluginConfiguration { Storefronts = priority };

        Assert.Equal(expected, config.GetStorefrontOrder());
    }

    [Theory]
    [InlineData("jp", "ja-jp")]
    [InlineData("us", "en-us")]
    public void GetLanguageFor_DerivesLanguageFromStorefront(string storefront, string expected)
    {
        var config = new PluginConfiguration();

        Assert.Equal(expected, config.GetLanguageFor(storefront));
    }

    [Fact]
    public void GetLanguageFor_HonoursOverride()
    {
        var config = new PluginConfiguration { LanguageOverride = "en-gb" };

        Assert.Equal("en-gb", config.GetLanguageFor("jp"));
        Assert.Equal("en-gb", config.GetLanguageFor("us"));
    }
}
