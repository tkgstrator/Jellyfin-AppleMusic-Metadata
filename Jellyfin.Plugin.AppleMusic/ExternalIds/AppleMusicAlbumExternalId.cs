using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.AppleMusic.ExternalIds;

/// <summary>
/// External link to an album on Apple Music.
/// </summary>
public class AppleMusicAlbumExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => PluginConstants.Name;

    /// <inheritdoc />
    public string Key => ProviderKeys.Album;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Album;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicAlbum;
}
