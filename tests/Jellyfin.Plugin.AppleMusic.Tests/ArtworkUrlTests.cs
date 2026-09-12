using System;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class ArtworkUrlTests
{
    // The shape Apple actually returns, captured from a live response.
    private const string RealTemplate =
        "https://is1-ssl.mzstatic.com/image/thumb/Music221/v4/e0/f9/f1/e0f9f1f3.jpg/{w}x{h}bb.jpg";

    [Fact]
    public void Resolve_SubstitutesBothDimensions()
    {
        var url = ArtworkUrl.Resolve(RealTemplate, 1400);

        Assert.Equal(
            "https://is1-ssl.mzstatic.com/image/thumb/Music221/v4/e0/f9/f1/e0f9f1f3.jpg/1400x1400bb.jpg",
            url);
    }

    [Fact]
    public void Resolve_SubstitutesCropAndFormatPlaceholders()
    {
        var url = ArtworkUrl.Resolve("https://example.invalid/art/{w}x{h}{c}.{f}", 300);

        Assert.Equal("https://example.invalid/art/300x300bb.jpg", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_ReturnsNullWithoutATemplate(string? template)
    {
        Assert.Null(ArtworkUrl.Resolve(template, 1400));
    }

    [Fact]
    public void Resolve_LeavesUrlsWithoutPlaceholdersAlone()
    {
        const string Plain = "https://example.invalid/art/cover.jpg";

        Assert.Equal(Plain, ArtworkUrl.Resolve(Plain, 1400));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_RejectsNonPositiveSizes(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtworkUrl.Resolve(RealTemplate, size));
    }

    [Fact]
    public void Resolve_UsesTheCropCodeAppleAsksFor()
    {
        var url = ArtworkUrl.Resolve("https://example.com/{w}x{h}{c}.{f}", 800, "ac");

        Assert.Equal("https://example.com/800x800ac.jpg", url);
    }

    [Fact]
    public void Resolve_FallsBackToTheDefaultCropWhenNoneIsGiven()
    {
        Assert.Equal("https://example.com/800x800bb.jpg", ArtworkUrl.Resolve("https://example.com/{w}x{h}{c}.{f}", 800, null));
        Assert.Equal("https://example.com/800x800bb.jpg", ArtworkUrl.Resolve("https://example.com/{w}x{h}{c}.{f}", 800, "  "));
    }
}
