using System.Linq;
using Jellyfin.Plugin.AppleMusic.ExternalIds;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class AppleMusicExternalUrlProviderTests
{
    [Fact]
    public void GetExternalUrls_BuildsAStorefrontAwareAlbumLink()
    {
        var album = new MusicAlbum();
        album.SetProviderId(ProviderKeys.Album, "1440791809");
        album.SetProviderId(ProviderKeys.Storefront, "jp");

        var url = Assert.Single(new AppleMusicExternalUrlProvider().GetExternalUrls(album));

        Assert.Equal("https://music.apple.com/jp/album/1440791809", url);
    }

    [Fact]
    public void GetExternalUrls_BuildsAnArtistLink()
    {
        var artist = new MusicArtist();
        artist.SetProviderId(ProviderKeys.Artist, "530814268");
        artist.SetProviderId(ProviderKeys.Storefront, "us");

        var url = Assert.Single(new AppleMusicExternalUrlProvider().GetExternalUrls(artist));

        Assert.Equal("https://music.apple.com/us/artist/530814268", url);
    }

    [Fact]
    public void GetExternalUrls_BuildsASongLink()
    {
        var song = new Audio();
        song.SetProviderId(ProviderKeys.Song, "1837658529");
        song.SetProviderId(ProviderKeys.Storefront, "jp");

        var url = Assert.Single(new AppleMusicExternalUrlProvider().GetExternalUrls(song));

        Assert.Equal("https://music.apple.com/jp/song/1837658529", url);
    }

    [Fact]
    public void GetExternalUrls_DefaultsToUsWhenTheStorefrontIsUnknown()
    {
        var album = new MusicAlbum();
        album.SetProviderId(ProviderKeys.Album, "1440791809");

        var url = Assert.Single(new AppleMusicExternalUrlProvider().GetExternalUrls(album));

        Assert.Equal("https://music.apple.com/us/album/1440791809", url);
    }

    [Fact]
    public void GetExternalUrls_YieldsNothingWithoutAnIdentifier()
    {
        var album = new MusicAlbum();

        Assert.Empty(new AppleMusicExternalUrlProvider().GetExternalUrls(album));
    }
}
