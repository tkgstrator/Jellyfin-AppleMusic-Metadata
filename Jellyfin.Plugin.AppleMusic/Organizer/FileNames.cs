using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Turns catalog names into names that are safe on every file system the
/// library might be shared from.
/// </summary>
public static partial class FileNames
{
    /// <summary>
    /// Characters Windows refuses in file names, and their full-width
    /// counterparts. Full-width keeps a Japanese title readable where an
    /// underscore would not.
    /// </summary>
    private static readonly (char From, char To)[] Replacements =
    [
        ('/', '／'),
        ('\\', '＼'),
        (':', '：'),
        ('*', '＊'),
        ('?', '？'),
        ('"', '”'),
        ('<', '＜'),
        ('>', '＞'),
        ('|', '｜'),
    ];

    /// <summary>
    /// Makes a name usable as a single path segment.
    /// </summary>
    /// <param name="name">Catalog name.</param>
    /// <returns>The sanitised name, never empty.</returns>
    public static string Sanitize(string? name)
    {
        var builder = new StringBuilder(name?.Length ?? 0);
        foreach (var c in name ?? string.Empty)
        {
            if (char.IsControl(c))
            {
                continue;
            }

            var replacement = Replacements.FirstOrDefault(pair => pair.From == c);
            builder.Append(replacement.To == '\0' ? c : replacement.To);
        }

        // Windows silently drops trailing dots and spaces, which would make the
        // name on disk differ from the one the plugin computed.
        var result = builder.ToString().Trim().TrimEnd('.').Trim();
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>
    /// Reads the disc and track numbers, and what is left as the title, off a
    /// file name such as <c>01 Title.flac</c>, <c>2-01 Title.flac</c>,
    /// <c>01 - Title.mp3</c> or plain <c>01.mp3</c>.
    /// </summary>
    /// <param name="path">File path or name.</param>
    /// <returns>The parts; numbers are null when the name does not start with one.</returns>
    /// <remarks>
    /// Jellyfin hands the providers a lookup built <em>before</em> it probes
    /// the tags, so on the first scan the only hint about which track a file
    /// is comes from its name.
    /// </remarks>
    public static (int? Disc, int? Track, string Title) ParseTrackFileName(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path) ?? string.Empty;
        var match = TrackPrefix().Match(name);
        if (!match.Success)
        {
            return (null, null, name.Trim());
        }

        int? disc = match.Groups["disc"].Success ? int.Parse(match.Groups["disc"].Value, CultureInfo.InvariantCulture) : null;
        var track = int.Parse(match.Groups["track"].Value, CultureInfo.InvariantCulture);
        return (disc, track, match.Groups["title"].Value.Trim());
    }

    /// <summary>
    /// Builds a track file name: <c>01 Title.flac</c>, or <c>2-01 Title.flac</c>
    /// on a multi-disc album.
    /// </summary>
    /// <param name="discNumber">Disc number, or null when unknown.</param>
    /// <param name="trackNumber">Track number, or null when unknown.</param>
    /// <param name="title">Track title.</param>
    /// <param name="extension">File extension including the dot.</param>
    /// <param name="multiDisc">Whether the album spans more than one disc.</param>
    /// <returns>The file name.</returns>
    public static string TrackFileName(int? discNumber, int? trackNumber, string? title, string extension, bool multiDisc)
    {
        var prefix = string.Empty;
        if (trackNumber is > 0)
        {
            prefix = trackNumber.Value.ToString("00", CultureInfo.InvariantCulture);
            if (multiDisc && discNumber is > 0)
            {
                prefix = discNumber.Value.ToString(CultureInfo.InvariantCulture) + "-" + prefix;
            }

            prefix += " ";
        }

        return prefix + Sanitize(title) + extension;
    }

    [GeneratedRegex(@"^\s*(?:(?<disc>\d{1,2})[-.])?(?<track>\d{1,3})(?:[\s._-]+|$)(?<title>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TrackPrefix();
}
