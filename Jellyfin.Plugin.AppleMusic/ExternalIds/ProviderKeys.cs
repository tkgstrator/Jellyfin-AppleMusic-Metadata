namespace Jellyfin.Plugin.AppleMusic.ExternalIds;

/// <summary>
/// Keys this plugin writes into <c>ProviderIds</c>.
/// </summary>
public static class ProviderKeys
{
    /// <summary>
    /// Apple Music catalog identifier of a song.
    /// </summary>
    public const string Song = "AppleMusicSong";

    /// <summary>
    /// Apple Music catalog identifier of an album.
    /// </summary>
    public const string Album = "AppleMusicAlbum";

    /// <summary>
    /// Apple Music catalog identifier of an artist.
    /// </summary>
    public const string Artist = "AppleMusicArtist";

    /// <summary>
    /// Storefront the stored identifiers belong to.
    /// </summary>
    /// <remarks>
    /// Catalog identifiers are storefront-scoped, so the storefront has to be
    /// remembered alongside them. No <c>IExternalId</c> implements this key, so
    /// it stays out of the item's external links in the UI.
    /// </remarks>
    public const string Storefront = "AppleMusicStorefront";
}
