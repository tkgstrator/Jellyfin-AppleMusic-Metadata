using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

/// <summary>
/// Jellyfin 12 installs a package into <c>plugins/&lt;name&gt;_&lt;version&gt;/</c> and refuses
/// names that are not valid directory names (Jellyfin 10.11 silently nests the directory
/// instead). The name is also baked into every published manifest and archive, so a
/// violation can only be fixed by a new release.
/// </summary>
public class PluginNameTests
{
    [Fact]
    public void PluginName_IsAValidDirectoryNameOnEveryPlatform()
    {
        // Path.GetInvalidFileNameChars() is platform dependent (only NUL and '/' on Linux),
        // so spell out the Windows set as well.
        char[] invalid = ['/', '\\', '<', '>', ':', '"', '|', '?', '*', '\0'];

        Assert.False(string.IsNullOrWhiteSpace(Plugin.PluginName));
        Assert.Equal(-1, Plugin.PluginName.IndexOfAny(invalid));
        Assert.Equal(-1, Plugin.PluginName.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.NotEqual(".", Plugin.PluginName);
        Assert.NotEqual("..", Plugin.PluginName);
    }

    [Fact]
    public void PluginName_MatchesTheReleaseMetadataTemplate()
    {
        var template = FindInParents(AppContext.BaseDirectory, Path.Combine("scripts", "meta.template.json"));
        Assert.True(template is not null, "scripts/meta.template.json not found above the test output directory");

        using var document = JsonDocument.Parse(File.ReadAllText(template));

        Assert.Equal(Plugin.PluginName, document.RootElement.GetProperty("name").GetString());
    }

    private static string? FindInParents(string start, string relative)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
