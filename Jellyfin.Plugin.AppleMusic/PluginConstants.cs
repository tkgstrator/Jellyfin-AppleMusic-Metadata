namespace Jellyfin.Plugin.AppleMusic;

/// <summary>
/// Names and URLs shared across the plugin.
/// </summary>
public static class PluginConstants
{
    /// <summary>
    /// Display name, also used as the provider name Jellyfin shows in library
    /// settings and on external links.
    /// </summary>
    public const string Name = "Apple Music";

    /// <summary>
    /// Base URL of the public Apple Music site. Deliberately storefront-less:
    /// Apple redirects to the visitor's region, and the format string used by
    /// external ids only carries the identifier.
    /// </summary>
    public const string WebBaseUrl = "https://music.apple.com";
}
