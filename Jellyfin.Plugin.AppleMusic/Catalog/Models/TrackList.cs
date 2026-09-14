using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// A page of an album's tracks.
/// </summary>
public class TrackList : ResourceList<SongAttributes>
{
    /// <summary>
    /// Gets or sets the relative URL of the next page, when the album has more
    /// tracks than fit in one response.
    /// </summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }
}
