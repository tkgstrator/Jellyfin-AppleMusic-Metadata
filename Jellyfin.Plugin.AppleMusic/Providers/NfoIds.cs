using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>
/// Reads the Apple Music ids Jellyfin keeps in <c>album.nfo</c>.
/// </summary>
/// <remarks>
/// Jellyfin hands an album or artist the ids from its own <c>.nfo</c> before
/// the providers run, but a track's lookup info carries nothing from its
/// parent album, so the song provider has to find the album itself. Reading
/// the same file the album was resolved from keeps a library that was tagged
/// with <c>tag-library.sh --nfo</c> — no files moved, play counts intact —
/// off the search endpoint entirely.
/// </remarks>
public static class NfoIds
{
    private const string AlbumIdElement = "applemusicalbumid";
    private const string StorefrontElement = "applemusicstorefrontid";

    /// <summary>
    /// Finds the album ids in the nearest <c>album.nfo</c> above a track.
    /// </summary>
    /// <param name="path">Path of the track file.</param>
    /// <param name="levels">How many parent directories to inspect.</param>
    /// <returns>The ids, both null when there is no usable nfo.</returns>
    public static (string? Id, string? Storefront) FindAlbumInAncestors(string? path, int levels = 2)
        => FindAlbumInAncestors(path, ReadIfExists, levels);

    /// <summary>
    /// Finds the album ids in the nearest <c>album.nfo</c> above a track,
    /// reading files through the given reader.
    /// </summary>
    /// <param name="path">Path of the track file.</param>
    /// <param name="read">Returns a file's content, or null when it is not there.</param>
    /// <param name="levels">How many parent directories to inspect.</param>
    /// <returns>The ids, both null when there is no usable nfo.</returns>
    public static (string? Id, string? Storefront) FindAlbumInAncestors(string? path, Func<string, string?> read, int levels = 2)
    {
        var current = path;
        for (var i = 0; i < levels && !string.IsNullOrEmpty(current); i++)
        {
            current = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(current))
            {
                break;
            }

            var ids = ParseAlbum(read(Path.Combine(current, "album.nfo")));
            if (ids.Id is not null)
            {
                return ids;
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Reads the album ids out of an nfo document.
    /// </summary>
    /// <param name="xml">Content of the nfo, or null.</param>
    /// <returns>The ids, both null when the document has no album id.</returns>
    public static (string? Id, string? Storefront) ParseAlbum(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return (null, null);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            // Hand-edited nfos are common enough that a broken one should cost
            // the track its id, not the scan.
            return (null, null);
        }

        var id = Value(document, AlbumIdElement);
        return id is null ? (null, null) : (id, Value(document, StorefrontElement));
    }

    private static string? Value(XDocument document, string name)
        => document.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

    private static string? ReadIfExists(string path)
        => File.Exists(path) ? File.ReadAllText(path) : null;
}
