namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// A single Apple Music catalog resource.
/// </summary>
/// <typeparam name="TAttributes">Attribute payload type for this resource kind.</typeparam>
public class Resource<TAttributes>
    where TAttributes : class
{
    /// <summary>
    /// Gets or sets the catalog identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource type, e.g. <c>songs</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attribute payload.
    /// </summary>
    public TAttributes? Attributes { get; set; }

    /// <summary>
    /// Gets or sets the related resources. Present on id lookups, absent on
    /// search results.
    /// </summary>
    public Relationships? Relationships { get; set; }
}
