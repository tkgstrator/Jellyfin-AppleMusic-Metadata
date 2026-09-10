using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// What the planner needs to know about an album on disk.
/// </summary>
/// <param name="Name">Name Jellyfin knows the album by, for messages.</param>
/// <param name="Path">Full path of the album directory.</param>
/// <param name="LibraryRoot">Full path of the library folder containing it.</param>
/// <param name="Tracks">The album's audio files.</param>
public sealed record AlbumSnapshot(string Name, string Path, string LibraryRoot, IReadOnlyList<TrackSnapshot> Tracks);
