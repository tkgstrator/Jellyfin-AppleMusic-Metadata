using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.AppleMusic.Catalog;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AppleMusic.ExternalIds;

/// <summary>
/// Builds the public Apple Music links shown on an item.
/// </summary>
/// <remarks>
/// A separate provider rather than a format string on the external ids,
/// because a correct link needs the storefront as well as the identifier, and
/// the two are stored under different keys.
/// </remarks>
public class AppleMusicExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => PluginConstants.Name;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var storefront = item.GetProviderId(ProviderKeys.Storefront);
        if (string.IsNullOrEmpty(storefront))
        {
            storefront = CatalogOptions.UnitedStates;
        }

        var key = item switch
        {
            MusicAlbum => ProviderKeys.Album,
            MusicArtist => ProviderKeys.Artist,
            Audio => ProviderKeys.Song,
            _ => null
        };

        if (key is null)
        {
            yield break;
        }

        var id = item.GetProviderId(key);
        if (string.IsNullOrEmpty(id))
        {
            yield break;
        }

        var path = key switch
        {
            ProviderKeys.Album => "album",
            ProviderKeys.Artist => "artist",
            _ => "song"
        };

        yield return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/{2}/{3}",
            PluginConstants.WebBaseUrl,
            storefront,
            path,
            id);
    }
}
