using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.AppleMusic.Tests;

/// <summary>
/// Builds the smallest MP4 files that carry the iTunes Store ids, laid out as
/// the store (and gamdl) writes them: ftyp, moov/udta/meta/ilst, then mdat.
/// </summary>
internal static class Mp4Fixture
{
    public const ulong Japan = 143462;

    public static byte[] Atom(string type, params byte[][] children)
    {
        var body = children.SelectMany(child => child).ToArray();
        var atom = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(atom, (uint)atom.Length);
        Encoding.Latin1.GetBytes(type).CopyTo(atom, 4);
        body.CopyTo(atom, 8);
        return atom;
    }

    // An atom whose header says 1 and carries the real size in 64 bits.
    public static byte[] LargeAtom(string type, byte[] body)
    {
        var atom = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(atom, 1);
        Encoding.Latin1.GetBytes(type).CopyTo(atom, 4);
        BinaryPrimitives.WriteUInt64BigEndian(atom.AsSpan(8), (ulong)atom.Length);
        body.CopyTo(atom, 16);
        return atom;
    }

    public static byte[] Integer(string name, ulong value, int length)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);

        // Type 21 (big-endian signed integer), then a zero locale.
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, 21);
        return Atom(name, Atom("data", header, bytes[(8 - length)..]));
    }

    public static byte[] Ilst(ulong? song = null, ulong? album = null, ulong? storefront = null)
    {
        var items = new List<byte[]> { Atom("©nam", Atom("data", new byte[8], "Idol"u8.ToArray())) };
        if (song is not null)
        {
            items.Add(Integer("cnID", song.Value, 4));
        }

        if (album is not null)
        {
            items.Add(Integer("plID", album.Value, 8));
        }

        if (storefront is not null)
        {
            items.Add(Integer("sfID", storefront.Value, 4));
        }

        return Atom("ilst", [.. items]);
    }

    public static byte[] Moov(byte[] ilst, bool fullBox = true)
    {
        var hdlr = Atom("hdlr", new byte[8], "mdirappl"u8.ToArray(), new byte[9]);
        var meta = fullBox
            ? Atom("meta", new byte[4], hdlr, ilst)
            : Atom("meta", hdlr, ilst);
        return Atom("moov", Atom("mvhd", new byte[100]), Atom("udta", meta));
    }

    public static byte[] M4a(ulong? song = null, ulong? album = null, ulong? storefront = null)
        => Concat(Ftyp(), Moov(Ilst(song, album, storefront)), Mdat());

    public static byte[] Ftyp() => Atom("ftyp", "M4A \0\0\0\0M4A mp42isom"u8.ToArray());

    public static byte[] Mdat() => Atom("mdat", new byte[64]);

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    public static MemoryStream Stream(byte[] bytes) => new(bytes, writable: false);
}
