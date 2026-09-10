using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.AppleMusic.ExternalIds;

/// <summary>
/// External link to an artist on Apple Music.
/// </summary>
public class AppleMusicArtistExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => PluginConstants.Name;

    /// <inheritdoc />
    public string Key => ProviderKeys.Artist;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Artist;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicArtist;
}
