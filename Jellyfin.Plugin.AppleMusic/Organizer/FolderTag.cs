using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// The <c>[amid-123]</c> marker that pins an Apple Music id to a directory name.
/// </summary>
/// <remarks>
/// A tagged directory lets the providers skip the search entirely and go
/// straight to the id lookup, which Apple does not rate limit the way it
/// limits search.
/// </remarks>
public static partial class FolderTag
{
    /// <summary>
    /// Appends the tag to a name, replacing any tag already present.
    /// </summary>
    /// <param name="name">Display name.</param>
    /// <param name="id">Apple Music catalog id.</param>
    /// <returns>The tagged name, e.g. <c>YANKEE-[amid-123]</c>.</returns>
    public static string Apply(string name, string id)
        => Strip(name) + "-[amid-" + id + "]";

    /// <summary>
    /// Removes the tag from a name.
    /// </summary>
    /// <param name="name">Possibly tagged name.</param>
    /// <returns>The name without the tag and the separator preceding it.</returns>
    public static string Strip(string name)
        => TagPattern().Replace(name, string.Empty).Trim();

    /// <summary>
    /// Extracts the id from a tagged name.
    /// </summary>
    /// <param name="name">Directory name, possibly tagged.</param>
    /// <returns>The id, or null when the name carries no tag.</returns>
    public static string? Parse(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var match = TagPattern().Match(name);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// Finds the nearest tagged directory above a path.
    /// </summary>
    /// <param name="path">Path of a file or directory.</param>
    /// <param name="levels">How many parent directories to inspect.</param>
    /// <returns>The id of the nearest tagged ancestor, or null.</returns>
    /// <remarks>
    /// A track usually sits directly in its album directory, but multi-disc
    /// rips often add a <c>CD1</c> level, hence the small walk upwards.
    /// </remarks>
    public static string? FindInAncestors(string? path, int levels = 2)
    {
        var current = path;
        for (var i = 0; i < levels && !string.IsNullOrEmpty(current); i++)
        {
            current = System.IO.Path.GetDirectoryName(current);
            var id = Parse(System.IO.Path.GetFileName(current));
            if (id is not null)
            {
                return id;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\s*-?\s*\[amid-(?<id>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}
