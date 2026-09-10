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

    [Fact]
    public void ToCatalogOptions_CarriesEverySetting()
    {
        var config = new PluginConfiguration
        {
            Storefronts = StorefrontPriority.UnitedStatesOnly,
            LanguageOverride = "en-gb",
            MaxSearchResults = 7,
            ArtworkSize = 600,
        };

        var options = config.ToCatalogOptions();

        Assert.Equal(["us"], options.Storefronts);
        Assert.Equal("en-gb", options.LanguageOverride);
        Assert.Equal(7, options.MaxSearchResults);
        Assert.Equal(600, options.ArtworkSize);
    }
}
