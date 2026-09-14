using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Outcome of a planning or organizing run.
/// </summary>
public class OrganizeReport
{
    /// <summary>
    /// Gets a value indicating whether nothing was touched on disk.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Gets the renames that were planned.
    /// </summary>
    public IReadOnlyList<PlannedMove> Moves { get; init; } = [];

    /// <summary>
    /// Gets what was left alone, with reasons.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>
    /// Gets the renames that failed, with reasons. Empty on a dry run.
    /// </summary>
    public IReadOnlyList<string> Failed { get; init; } = [];

    /// <summary>
    /// Gets how many renames were applied. Zero on a dry run.
    /// </summary>
    public int Applied { get; init; }

    /// <summary>
    /// Gets how many albums carried an Apple Music id and were considered.
    /// </summary>
    public int Albums { get; init; }

    /// <summary>
    /// Gets the same work grouped by artist, in name order.
    /// </summary>
    /// <remarks>
    /// <see cref="Moves"/> is the list the executor walks, in apply order. A
    /// few thousand paths in one flat list is unreadable, so the same work is
    /// also reported artist by artist: how many albums each contributes, and
    /// what each album becomes.
    /// </remarks>
    public IReadOnlyList<ArtistOutcome> Artists { get; init; } = [];

    /// <summary>
    /// Gets how many distinct artists the run touched.
    /// </summary>
    public int ArtistCount => Artists.Count;

    /// <summary>
    /// Rolls the per-album plans up by artist, for <see cref="Artists"/>.
    /// </summary>
    /// <remarks>
    /// Albums the catalog could not resolve carry no artist, so they land
    /// under an empty name and are reported at the end rather than dropped.
    /// </remarks>
    /// <param name="plans">The per-album plans, in any order.</param>
    /// <returns>The same work, artist by artist in name order.</returns>
    public static IReadOnlyList<ArtistOutcome> GroupByArtist(IReadOnlyList<AlbumPlan> plans)
        => plans
            .GroupBy(plan => (plan.Artist, plan.ArtistId))
            .Select(group => new ArtistOutcome
            {
                Artist = group.Key.Artist,
                ArtistId = group.Key.ArtistId,
                Albums = group
                    .Select(plan => new AlbumOutcome
                    {
                        Album = plan.Album,
                        AlbumId = plan.AlbumId,
                        TargetName = plan.TargetAlbumDirectory.Length == 0
                            ? string.Empty
                            : Path.GetFileName(Path.TrimEndingDirectorySeparator(plan.TargetAlbumDirectory)),
                        DirectoryMoves = plan.Moves.Any(move => move.Kind == MoveKind.Directory),
                        TrackRenames = plan.Moves.Count(move => move.Kind == MoveKind.File),
                        Skipped = plan.Skipped,
                    })
                    .OrderBy(album => album.Album, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderBy(artist => artist.Artist.Length == 0)
            .ThenBy(artist => artist.Artist, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
