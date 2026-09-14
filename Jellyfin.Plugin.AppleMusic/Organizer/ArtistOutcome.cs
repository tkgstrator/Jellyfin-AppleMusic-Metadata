using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Everything the organizer intends for one artist, so a report can be read
/// artist by artist rather than as one flat list of paths.
/// </summary>
public class ArtistOutcome
{
    /// <summary>
    /// Gets the artist name as Apple Music spells it.
    /// </summary>
    public string Artist { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Apple Music identifier of the artist.
    /// </summary>
    public string ArtistId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the artist's albums, in name order.
    /// </summary>
    public IReadOnlyList<AlbumOutcome> Albums { get; init; } = [];

    /// <summary>
    /// Gets how many albums this artist contributes.
    /// </summary>
    public int AlbumCount => Albums.Count;

    /// <summary>
    /// Gets how many of them move.
    /// </summary>
    public int MovedAlbums => Albums.Count(album => album.DirectoryMoves);

    /// <summary>
    /// Gets how many track files are renamed across them.
    /// </summary>
    public int TrackRenames => Albums.Sum(album => album.TrackRenames);

    /// <summary>
    /// Gets how many things were left alone across them.
    /// </summary>
    public int SkippedCount => Albums.Sum(album => album.Skipped.Count);
}
