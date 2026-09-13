using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;
using Jellyfin.Plugin.AppleMusic.Providers;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AppleMusic.Tests;

public class EmbeddedProviderTests
{
    [Fact]
    public void Apply_ReplacesWrongValuesWithoutUnionAndIsIdempotent()
    {
        var audio = new Audio { Name = "Untouched", IndexNumber = 85, Artists = ["Wrong"], Album = "Wrong", AlbumArtists = ["Wrong"], Genres = ["Wrong"], ProductionYear = 2000, ParentIndexNumber = 3 };
        var tags = new EmbeddedMetadata { Track = 1, Artists = ["Right"], Album = "Right", AlbumArtists = ["Right"], Genres = ["Right"], Year = 2024, Disc = 2 };
        Assert.Equal(ItemUpdateType.MetadataImport, EmbeddedAudioMetadataProvider.Apply(audio, tags));
        Assert.Equal(1, audio.IndexNumber);
        Assert.Equal(2, audio.ParentIndexNumber);
        Assert.Equal(["Right"], audio.Artists);
        Assert.Equal(["Right"], audio.AlbumArtists);
        Assert.Equal(["Right"], audio.Genres);
        Assert.Equal("Right", audio.Album);
        Assert.Equal(2024, audio.ProductionYear);
        Assert.Equal("Untouched", audio.Name);
        Assert.Equal(ItemUpdateType.None, EmbeddedAudioMetadataProvider.Apply(audio, tags));
    }

    [Fact]
    public void Apply_RespectsLocksAndMissingValues()
    {
        var audio = new Audio { IndexNumber = 18, Artists = ["Manual"], AlbumArtists = ["Manual"], Genres = ["Manual"], LockedFields = [MetadataField.Cast, MetadataField.Genres] };
        var tags = new EmbeddedMetadata { Artists = ["Tag"], AlbumArtists = ["Tag"], Genres = ["Tag"] };
        Assert.Equal(ItemUpdateType.None, EmbeddedAudioMetadataProvider.Apply(audio, tags));
        Assert.Equal(18, audio.IndexNumber);
        Assert.Equal(["Manual"], audio.Artists);
        Assert.Equal(["Manual"], audio.Genres);
        audio.IsLocked = true;
        Assert.Equal(ItemUpdateType.None, EmbeddedAudioMetadataProvider.Apply(audio, new EmbeddedMetadata { Track = 1 }));
    }

    [Fact]
    public async Task Fetch_SkipsLockedFilesBeforeReading()
    {
        var reader = new Reader((_, _, _) => throw new InvalidOperationException("Must not read"));
        var provider = Create(reader);
        Assert.Equal(ItemUpdateType.None, await provider.FetchAsync(new Audio { Path = "/music/file.flac", IsLocked = true }, null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetch_RestoresTagsAfterMissingOrWrongRemoteResult(bool matched)
    {
        var audio = new Audio { Path = "/music/file.flac", IndexNumber = matched ? 85 : null, Artists = matched ? ["Wrong"] : [] };
        var reader = new Reader((_, _, _) => Task.FromResult(new EmbeddedMetadata { Track = 1, Artists = ["Tag Artist"] }));
        Assert.Equal(ItemUpdateType.MetadataImport, await Create(reader).FetchAsync(audio, null!, TestContext.Current.CancellationToken));
        Assert.Equal(1, audio.IndexNumber);
        Assert.Equal(["Tag Artist"], audio.Artists);
    }

    [Fact]
    public async Task Fetch_ReadFailureKeepsExistingFields()
    {
        var audio = new Audio { Path = "/music/file.flac", IndexNumber = 18 };
        var reader = new Reader((_, _, _) => throw new IOException("Broken file"));
        Assert.Equal(ItemUpdateType.None, await Create(reader).FetchAsync(audio, null!, TestContext.Current.CancellationToken));
        Assert.Equal(18, audio.IndexNumber);
    }

    [Fact]
    public async Task Fetch_RechecksCancellationAndLockAfterReading()
    {
        var audio = new Audio { Path = "/music/file.flac", IndexNumber = 18 };
        using var cancelled = new CancellationTokenSource();
        var reader = new Reader(async (_, _, _) =>
        {
            await cancelled.CancelAsync();
            return new EmbeddedMetadata { Track = 1 };
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(reader).FetchAsync(audio, null!, cancelled.Token));
        Assert.Equal(18, audio.IndexNumber);
        reader = new Reader((_, _, _) =>
        {
            audio.IsLocked = true;
            return Task.FromResult(new EmbeddedMetadata { Track = 1 });
        });
        Assert.Equal(ItemUpdateType.None, await Create(reader).FetchAsync(audio, null!, TestContext.Current.CancellationToken));
        Assert.Equal(18, audio.IndexNumber);
    }

    [Fact]
    public void Options_OnlyEnablesConfiguredDelimiters()
    {
        var options = new LibraryOptions { CustomTagDelimiters = ["/"], DelimiterWhitelist = ["AC/DC"], PreferNonstandardArtistsTag = true };
        Assert.Empty(EmbeddedAudioMetadataProvider.ToTagOptions(options).Delimiters);
        options.UseCustomTagDelimiters = true;
        var result = EmbeddedAudioMetadataProvider.ToTagOptions(options);
        Assert.Equal(["/"], result.Delimiters);
        Assert.Equal(["AC/DC"], result.Whitelist);
        Assert.True(result.PreferNonstandardArtists);
    }

    [Fact]
    public void Registration_UsesOneSharedReaderAndPostMergeProviderContract()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);
        var reader = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEmbeddedMetadataReader));
        Assert.Equal(ServiceLifetime.Singleton, reader.Lifetime);
        Assert.True(typeof(ICustomMetadataProvider<Audio>).IsAssignableFrom(typeof(EmbeddedAudioMetadataProvider)));
        Assert.False(typeof(IPreRefreshProvider).IsAssignableFrom(typeof(EmbeddedAudioMetadataProvider)));
        Assert.False(typeof(IForcedProvider).IsAssignableFrom(typeof(EmbeddedAudioMetadataProvider)));
    }

    private static EmbeddedAudioMetadataProvider Create(IEmbeddedMetadataReader reader)
        => new(reader, _ => new LibraryOptions(), NullLogger<EmbeddedAudioMetadataProvider>.Instance, item => Path.IsPathFullyQualified(item.Path));

    private sealed class Reader(Func<string, EmbeddedTagOptions, CancellationToken, Task<EmbeddedMetadata>> read) : IEmbeddedMetadataReader
    {
        public Task<EmbeddedMetadata> ReadAsync(string path, EmbeddedTagOptions options, CancellationToken cancellationToken)
            => read(path, options, cancellationToken);
    }
}
