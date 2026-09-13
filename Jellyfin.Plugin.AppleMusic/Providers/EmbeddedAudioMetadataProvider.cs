using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using TagSnapshot = Jellyfin.Plugin.AppleMusic.Catalog.EmbeddedMetadata.EmbeddedMetadata;

namespace Jellyfin.Plugin.AppleMusic.Providers;

/// <summary>Restores explicitly tagged fields after remote metadata has been merged.</summary>
public sealed class EmbeddedAudioMetadataProvider : ICustomMetadataProvider<Audio>
{
    private readonly IEmbeddedMetadataReader _reader;
    private readonly Func<Audio, bool> _isFile;
    private readonly Func<Audio, LibraryOptions> _options;
    private readonly ILogger<EmbeddedAudioMetadataProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="EmbeddedAudioMetadataProvider"/> class.</summary>
    /// <param name="reader">Shared read-only tag reader.</param>
    /// <param name="libraryManager">Library-specific tag settings.</param>
    /// <param name="logger">Diagnostic logger.</param>
    public EmbeddedAudioMetadataProvider(IEmbeddedMetadataReader reader, ILibraryManager libraryManager, ILogger<EmbeddedAudioMetadataProvider> logger)
        : this(reader, item => libraryManager.GetLibraryOptions(item), logger)
    {
    }

    internal EmbeddedAudioMetadataProvider(IEmbeddedMetadataReader reader, Func<Audio, LibraryOptions> options, ILogger<EmbeddedAudioMetadataProvider> logger, Func<Audio, bool>? isFile = null)
    {
        _isFile = isFile ?? (item => item.IsFileProtocol);
        _reader = reader;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Embedded Audio Metadata";

    /// <inheritdoc />
    public async Task<ItemUpdateType> FetchAsync(Audio item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.IsLocked || item.IsVirtualItem || item.IsShortcut || string.IsNullOrWhiteSpace(item.Path) || !_isFile(item))
        {
            return ItemUpdateType.None;
        }

        try
        {
            var tags = await _reader.ReadAsync(item.Path, ToTagOptions(_options(item)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var field in tags.Conflicts)
            {
                _logger.LogWarning("Conflicting embedded {Field} in {Path}; preserving the existing field", field, item.Path);
            }

            return Apply(item, tags);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(exception, "Could not read embedded metadata from {Path}; preserving existing fields", item.Path);
            return ItemUpdateType.None;
        }
    }

    internal static EmbeddedTagOptions ToTagOptions(LibraryOptions options)
        => new()
        {
            PreferNonstandardArtists = options.PreferNonstandardArtistsTag,
            Delimiters = options.UseCustomTagDelimiters ? options.CustomTagDelimiters : [],
            Whitelist = options.DelimiterWhitelist
        };

    internal static ItemUpdateType Apply(Audio item, TagSnapshot tags)
    {
        if (item.IsLocked)
        {
            return ItemUpdateType.None;
        }

        var changed = false;
        if (!item.LockedFields.Contains(MetadataField.Cast))
        {
            if (tags.Artists.Count > 0 && !TagSnapshot.SameList(item.Artists, tags.Artists))
            {
                item.Artists = tags.Artists.ToArray();
                changed = true;
            }

            if (tags.AlbumArtists.Count > 0 && !TagSnapshot.SameList(item.AlbumArtists, tags.AlbumArtists))
            {
                item.AlbumArtists = tags.AlbumArtists.ToArray();
                changed = true;
            }
        }

        if (!item.LockedFields.Contains(MetadataField.Genres) && tags.Genres.Count > 0 && !TagSnapshot.SameList(item.Genres, tags.Genres))
        {
            item.Genres = tags.Genres.ToArray();
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(tags.Album) && !TagSnapshot.SameText(item.Album, tags.Album))
        {
            item.Album = tags.Album;
            changed = true;
        }

        if (tags.Track > 0 && item.IndexNumber != tags.Track)
        {
            item.IndexNumber = tags.Track;
            changed = true;
        }

        if (tags.Disc > 0 && item.ParentIndexNumber != tags.Disc)
        {
            item.ParentIndexNumber = tags.Disc;
            changed = true;
        }

        if (tags.Year is > 0 and <= 9999 && item.ProductionYear != tags.Year)
        {
            item.ProductionYear = tags.Year;
            changed = true;
        }

        return changed ? ItemUpdateType.MetadataImport : ItemUpdateType.None;
    }
}
