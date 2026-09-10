using System.Collections.Generic;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Models;

/// <summary>
/// A list of resources. Used both for search result categories and for the
/// envelope of an id lookup.
/// </summary>
/// <typeparam name="TAttributes">Attribute payload type for this resource kind.</typeparam>
public class ResourceList<TAttributes>
    where TAttributes : class
{
    /// <summary>
    /// Gets or sets the resources.
    /// </summary>
    public IReadOnlyList<Resource<TAttributes>> Data { get; set; } = [];
}
