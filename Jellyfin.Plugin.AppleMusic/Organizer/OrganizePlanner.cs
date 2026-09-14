using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.Catalog.Models;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Decides where an album and its tracks should live. Pure: touches no disk
/// and knows nothing about Jellyfin, so every rule here is unit tested.
/// </summary>
/// <remarks>
/// Target layout, relative to the library folder:
/// <code>
/// {Artist}-[amid-{artistId}]/{Album}-[amid-{albumId}]/{NN} {Title}.{ext}
/// </code>
/// The album directory moves as a whole so cover art, cue sheets and the like
/// travel with it. Tracks are then renamed inside it, which also flattens
/// per-disc sub-directories.
/// </remarks>
public class OrganizePlanner
{
    private readonly Func<string, bool> _exists;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizePlanner"/> class.
    /// </summary>
    /// <param name="exists">
    /// Tells whether a path already exists on disk, so the planner can refuse
    /// to overwrite anything. Injected so tests need no file system.
    /// </param>
    public OrganizePlanner(Func<string, bool> exists)
    {
        _exists = exists;
    }

    /// <summary>
    /// Plans the moves for one album.
    /// </summary>
    /// <param name="album">The album as it is on disk.</param>
    /// <param name="catalogAlbum">The album as Apple Music knows it, with its tracks.</param>
    /// <param name="artistName">Display name of the album artist.</param>
    /// <param name="artistId">Apple Music id of the album artist.</param>
    /// <param name="options">Organizer settings.</param>
    /// <returns>The plan.</returns>
    public AlbumPlan Plan(
        AlbumSnapshot album,
        CatalogItem<AlbumAttributes> catalogAlbum,
        string artistName,
        string artistId,
        OrganizeOptions options)
    {
        var moves = new List<PlannedMove>();
        var skipped = new List<string>();

        var root = Path.TrimEndingDirectorySeparator(album.LibraryRoot);
        var currentDir = Path.TrimEndingDirectorySeparator(album.Path);

        var targetArtistDir = Path.Combine(root, FolderTag.Apply(FileNames.Sanitize(artistName), artistId));
        var targetAlbumDir = Path.Combine(targetArtistDir, FolderTag.Apply(FileNames.Sanitize(catalogAlbum.Attributes.Name), catalogAlbum.Id));

        if (!IsInside(root, currentDir))
        {
            skipped.Add($"{album.Name}: {currentDir} is not inside the library folder {root}");
            return new AlbumPlan(album.Name, moves, skipped, null, targetArtistDir);
        }

        if (album.Tracks.Any(track => !IsInside(currentDir, track.Path)))
        {
            skipped.Add($"{album.Name}: not every track lives under {currentDir}, leaving the album alone");
            return new AlbumPlan(album.Name, moves, skipped, null, targetArtistDir);
        }

        var albumMoves = !PathEquals(currentDir, targetAlbumDir);
        if (albumMoves)
        {
            if (_exists(targetAlbumDir))
            {
                skipped.Add($"{album.Name}: {targetAlbumDir} already exists");
                return new AlbumPlan(album.Name, moves, skipped, null, targetArtistDir);
            }

            moves.Add(new PlannedMove(MoveKind.Directory, currentDir, targetAlbumDir));
        }

        if (options.RenameTrackFiles)
        {
            PlanTracks(album, catalogAlbum, currentDir, targetAlbumDir, moves, skipped);
        }

        var vacated = albumMoves ? Path.GetDirectoryName(currentDir) : null;
        if (vacated is not null && PathEquals(vacated, root))
        {
            vacated = null;
        }

        return new AlbumPlan(album.Name, moves, skipped, vacated, targetArtistDir);
    }

    private static bool IsInside(string parent, string path)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.Ordinal);

    private static CatalogItem<SongAttributes>? Match(TrackSnapshot track, IReadOnlyList<CatalogItem<SongAttributes>> candidates)
    {
        if (!string.IsNullOrEmpty(track.AppleMusicId))
        {
            var byId = candidates.FirstOrDefault(candidate => candidate.Id == track.AppleMusicId);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (track.TrackNumber is null)
        {
            return null;
        }

        // Disc numbers are often missing from tags on single-disc albums.
        var byNumber = candidates
            .Where(candidate => candidate.Attributes.TrackNumber == track.TrackNumber
                && (track.DiscNumber is null || candidate.Attributes.DiscNumber is null || candidate.Attributes.DiscNumber == track.DiscNumber))
            .ToList();
        return byNumber.Count == 1 ? byNumber[0] : null;
    }

    private void PlanTracks(
        AlbumSnapshot album,
        CatalogItem<AlbumAttributes> catalogAlbum,
        string currentDir,
        string targetAlbumDir,
        List<PlannedMove> moves,
        List<string> skipped)
    {
        var multiDisc = catalogAlbum.Tracks
            .Select(track => track.Attributes.DiscNumber ?? 1)
            .Distinct()
            .Count() > 1;

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in album.Tracks)
        {
            var catalogTrack = Match(track, catalogAlbum.Tracks);
            if (catalogTrack is null)
            {
                skipped.Add($"{album.Name}: could not tell which Apple Music track {Path.GetFileName(track.Path)} is");
                continue;
            }

            // Where the file will be once the album directory has moved.
            var from = Path.Combine(targetAlbumDir, Path.GetRelativePath(currentDir, track.Path));
            var to = Path.Combine(targetAlbumDir, FileNames.TrackFileName(
                catalogTrack.Attributes.DiscNumber,
                catalogTrack.Attributes.TrackNumber,
                catalogTrack.Attributes.Name,
                Path.GetExtension(track.Path),
                multiDisc));

            if (PathEquals(from, to))
            {
                claimed.Add(to);
                continue;
            }

            if (!claimed.Add(to))
            {
                skipped.Add($"{album.Name}: two tracks would both become {Path.GetFileName(to)}");
                continue;
            }

            // Only meaningful when the album stays put; after a directory move
            // nothing exists at the target yet.
            if (PathEquals(currentDir, targetAlbumDir) && _exists(to))
            {
                skipped.Add($"{album.Name}: {to} already exists");
                continue;
            }

            moves.Add(new PlannedMove(MoveKind.File, from, to));
        }
    }
}
