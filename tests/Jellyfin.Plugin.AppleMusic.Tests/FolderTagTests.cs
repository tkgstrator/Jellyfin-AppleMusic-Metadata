using Jellyfin.Plugin.AppleMusic.Organizer;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class FolderTagTests
{
    [Theory]
    [InlineData("YANKEE-[amid-123]", "123")]
    [InlineData("YANKEE [amid-123]", "123")]
    [InlineData("[amid-9] YANKEE", "9")]
    [InlineData("YANKEE", null)]
    [InlineData("YANKEE-[amid-]", null)]
    [InlineData("YANKEE-[amid-abc]", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Parse_FindsTheIdAnywhereInTheName(string? name, string? expected)
        => Assert.Equal(expected, FolderTag.Parse(name));

    [Fact]
    public void Apply_AppendsTheTag()
        => Assert.Equal("YANKEE-[amid-123]", FolderTag.Apply("YANKEE", "123"));

    [Fact]
    public void Apply_ReplacesAnExistingTag()
        => Assert.Equal("YANKEE-[amid-456]", FolderTag.Apply("YANKEE-[amid-123]", "456"));

    [Theory]
    [InlineData("YANKEE-[amid-123]", "YANKEE")]
    [InlineData("YANKEE [amid-123]", "YANKEE")]
    [InlineData("YANKEE", "YANKEE")]
    public void Strip_RemovesTheTagAndSeparator(string name, string expected)
        => Assert.Equal(expected, FolderTag.Strip(name));
}
