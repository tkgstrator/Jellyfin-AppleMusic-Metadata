namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// What the planner needs to know about a track on disk.
/// </summary>
/// <param name="Path">Full path of the audio file.</param>
/// <param name="AppleMusicId">Apple Music song id Jellyfin stored, if any.</param>
/// <param name="DiscNumber">Disc number from the tags, if any.</param>
/// <param name="TrackNumber">Track number from the tags, if any.</param>
/// <param name="Name">Title Jellyfin knows the track by.</param>
public sealed record TrackSnapshot(string Path, string? AppleMusicId, int? DiscNumber, int? TrackNumber, string? Name);
