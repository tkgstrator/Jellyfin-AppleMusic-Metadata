using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// The organizer's intentions for one album.
/// </summary>
/// <param name="Album">Name Jellyfin knows the album by.</param>
/// <param name="Moves">Renames to apply, in order.</param>
/// <param name="Skipped">Things left alone, with the reason.</param>
/// <param name="VacatedDirectory">
/// Directory the album leaves behind, when it moves out of its current parent.
/// The executor deletes it once empty. Null when the album stays put.
/// </param>
/// <param name="TargetArtistDirectory">Artist directory the album ends up in.</param>
public sealed record AlbumPlan(
    string Album,
    IReadOnlyList<PlannedMove> Moves,
    IReadOnlyList<string> Skipped,
    string? VacatedDirectory,
    string TargetArtistDirectory);
