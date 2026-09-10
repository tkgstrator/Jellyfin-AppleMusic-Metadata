using System.Text.Json;

namespace Jellyfin.Plugin.AppleMusic.Catalog;

/// <summary>
/// Serializer settings shared by every catalog response.
/// </summary>
public static class CatalogJson
{
    /// <summary>
    /// Gets the options used to deserialize Apple Music responses, whose
    /// property names are camelCase.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
