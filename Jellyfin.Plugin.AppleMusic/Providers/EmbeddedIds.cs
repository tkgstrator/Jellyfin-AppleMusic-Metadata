using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.AppleMusic.Catalog;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>
/// Reads the Apple Music ids the iTunes Store writes into an MP4 file.
/// </summary>
/// <remarks>
/// Files bought from the iTunes Store, or saved by downloaders that copy its
/// tags (gamdl, for one), carry the catalog ids in the <c>ilst</c> atom:
/// <c>cnID</c> for the song, <c>plID</c> for the album and <c>sfID</c> for the
/// storefront. Jellyfin's own tag reader drops them, so without this the
/// provider would search for a track whose id is sitting in the file. Only
/// the atom headers are read — the artwork and the audio are skipped with a
/// seek — so this costs a few small reads per file, and no tag library has
/// to be shipped with the plugin.
/// </remarks>
public static class EmbeddedIds
{
    private const int HeaderSize = 8;

    private static readonly HashSet<string> Mp4Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m4a", ".m4b", ".m4p", ".mp4",
    };

    /// <summary>
    /// Reads the ids embedded in a file. Files that are not MP4 are not opened.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The ids, all null when the file has none or cannot be read.</returns>
    public static (string? Song, string? Album, string? Storefront) Read(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Mp4Extensions.Contains(Path.GetExtension(path)))
        {
            return (null, null, null);
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Parse(stream);
        }
        catch (IOException)
        {
            // A missing, locked or truncated file costs the track its ids, not
            // the scan.
            return (null, null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Finds the album id embedded in the tracks of an album directory,
    /// including the tracks in its disc subdirectories.
    /// </summary>
    /// <param name="directory">Path of the album directory.</param>
    /// <returns>The album id and storefront, both null when no track has them.</returns>
    public static (string? Id, string? Storefront) FindAlbumInDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return (null, null);
        }

        try
        {
            var tracks = Directory.EnumerateFiles(directory)
                .Order(StringComparer.Ordinal)
                .Concat(Directory.EnumerateDirectories(directory)
                    .Order(StringComparer.Ordinal)
                    .SelectMany(disc => Directory.EnumerateFiles(disc).Order(StringComparer.Ordinal)));

            foreach (var track in tracks)
            {
                var ids = Read(track);
                if (ids.Album is not null)
                {
                    return (ids.Album, ids.Storefront);
                }
            }
        }
        catch (IOException)
        {
            // The directory went away mid-scan; fall back to the next source.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return (null, null);
    }

    /// <summary>
    /// Reads the ids out of an MP4 stream.
    /// </summary>
    /// <param name="stream">A readable, seekable stream positioned anywhere.</param>
    /// <returns>The ids, all null when the stream has no iTunes metadata.</returns>
    public static (string? Song, string? Album, string? Storefront) Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var moov = FindChild(stream, 0, stream.Length, "moov");
        var udta = moov is null ? null : FindChild(stream, moov.Value.Start, moov.Value.End, "udta");
        var meta = udta is null ? null : FindChild(stream, udta.Value.Start, udta.Value.End, "meta");
        if (meta is null)
        {
            return (null, null, null);
        }

        var ilst = FindChild(stream, MetaChildrenStart(stream, meta.Value), meta.Value.End, "ilst");
        if (ilst is null)
        {
            return (null, null, null);
        }

        string? song = null;
        string? album = null;
        string? storefront = null;
        foreach (var item in Children(stream, ilst.Value.Start, ilst.Value.End))
        {
            switch (item.Type)
            {
                case "cnID":
                    song = ReadInteger(stream, item)?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "plID":
                    album = ReadInteger(stream, item)?.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "sfID":
                    storefront = ToStorefront(ReadInteger(stream, item));
                    break;
            }
        }

        return (song, album, storefront);
    }

    /// <summary>
    /// Maps the numeric storefront id the iTunes Store writes to the code the
    /// catalog API takes.
    /// </summary>
    /// <param name="id">Numeric storefront id.</param>
    /// <returns>
    /// The storefront code, or null for a storefront the plugin does not query,
    /// so the lookup falls back to the configured order.
    /// </returns>
    public static string? ToStorefront(ulong? id) => id switch
    {
        143462 => CatalogOptions.Japan,
        143441 => CatalogOptions.UnitedStates,
        _ => null,
    };

    private static long MetaChildrenStart(Stream stream, Atom meta)
    {
        // iTunes writes meta as a full box (4 bytes of version and flags before
        // the children); QuickTime writes it as a plain one. Either way the
        // first child is hdlr, so look for it.
        Span<byte> head = stackalloc byte[HeaderSize];
        if (meta.End - meta.Start >= HeaderSize)
        {
            stream.Position = meta.Start;
            stream.ReadExactly(head);
            if (Encoding.ASCII.GetString(head[4..]) == "hdlr")
            {
                return meta.Start;
            }
        }

        return meta.Start + 4;
    }

    private static Atom? FindChild(Stream stream, long start, long end, string type)
    {
        foreach (var child in Children(stream, start, end))
        {
            if (child.Type == type)
            {
                return child;
            }
        }

        return null;
    }

    private static IEnumerable<Atom> Children(Stream stream, long start, long end)
    {
        var position = start;
        while (end - position >= HeaderSize)
        {
            var atom = ReadHeader(stream, position, end);
            if (atom is null)
            {
                yield break;
            }

            yield return atom.Value;
            position = atom.Value.End;
        }
    }

    private static Atom? ReadHeader(Stream stream, long position, long end)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.Position = position;
        stream.ReadExactly(header);

        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = Encoding.Latin1.GetString(header[4..]);
        var headerSize = HeaderSize;

        if (size == 1)
        {
            // 64-bit size, used by atoms over 4 GiB.
            if (end - position < HeaderSize * 2)
            {
                return null;
            }

            Span<byte> large = stackalloc byte[HeaderSize];
            stream.ReadExactly(large);
            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(large);
            size = largeSize > long.MaxValue ? -1 : (long)largeSize;
            headerSize = HeaderSize * 2;
        }
        else if (size == 0)
        {
            // Runs to the end of the enclosing atom.
            size = end - position;
        }

        if (size < headerSize || size > end - position)
        {
            // Corrupt or truncated; stop rather than read garbage.
            return null;
        }

        return new Atom(type, position + headerSize, position + size);
    }

    private static ulong? ReadInteger(Stream stream, Atom item)
    {
        // Each ilst item holds a data atom: 4 bytes of type, 4 bytes of locale,
        // then the big-endian value.
        var data = FindChild(stream, item.Start, item.End, "data");
        if (data is null)
        {
            return null;
        }

        var length = data.Value.End - data.Value.Start - HeaderSize;
        if (length is < 1 or > 8)
        {
            return null;
        }

        Span<byte> value = stackalloc byte[8];
        value.Clear();
        stream.Position = data.Value.Start + HeaderSize;
        stream.ReadExactly(value[(8 - (int)length)..]);
        var number = BinaryPrimitives.ReadUInt64BigEndian(value);
        return number == 0 ? null : number;
    }

    private readonly record struct Atom(string Type, long Start, long End);
}
