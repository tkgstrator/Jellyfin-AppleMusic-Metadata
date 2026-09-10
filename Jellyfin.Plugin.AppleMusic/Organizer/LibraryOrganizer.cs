using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AppleMusic.Catalog;
using Jellyfin.Plugin.AppleMusic.ExternalIds;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Organizer;

/// <summary>
/// Moves albums into the <c>Artist-[amid-id]/Album-[amid-id]</c> layout.
/// Gathers what Jellyfin knows, lets <see cref="OrganizePlanner"/> decide, and
/// applies the result; the decisions themselves live in the planner so they
/// can be unit tested.
/// </summary>
/// <remarks>
/// Jellyfin is not told about the moves. Paths change, so the next library
/// scan drops the old items and creates new ones — which the providers then
/// resolve by the ids in the directory names, without a single search.
/// Per-user data on the tracks (play counts, favourites) does not survive;
/// this is documented as the trade-off of not rewriting Jellyfin's database.
/// </remarks>
public sealed class LibraryOrganizer : IDisposable
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".wav", ".wma", ".aif", ".aiff", ".ape", ".wv", ".dsf", ".dff", ".mpc", ".alac",
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IAppleMusicCatalog _catalog;
    private readonly Func<OrganizeOptions> _options;
    private readonly ILogger<LibraryOrganizer> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryOrganizer"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="catalog">Apple Music catalog.</param>
    /// <param name="options">Supplies the current options.</param>
    /// <param name="logger">Logger.</param>
    public LibraryOrganizer(
        ILibraryManager libraryManager,
        IAppleMusicCatalog catalog,
        Func<OrganizeOptions> options,
        ILogger<LibraryOrganizer> logger)
    {
        _libraryManager = libraryManager;
        _catalog = catalog;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispose()
        => _running.Dispose();

    /// <summary>
    /// Plans and, unless this is a dry run, applies the moves.
    /// </summary>
    /// <param name="dryRun">When true, only report what would happen.</param>
    /// <param name="progress">Progress, 0–100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was planned and done.</returns>
    /// <exception cref="InvalidOperationException">Another run is in progress.</exception>
    public async Task<OrganizeReport> RunAsync(bool dryRun, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _running.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException("The organizer is already running.");
        }

        try
        {
            var plans = await PlanAsync(progress, cancellationToken);
            var moves = plans.SelectMany(plan => plan.Moves).ToList();
            var skipped = plans.SelectMany(plan => plan.Skipped).ToList();

            if (dryRun)
            {
                foreach (var move in moves)
                {
                    _logger.LogInformation("[dry run] would move {Kind} {From} -> {To}", move.Kind, move.From, move.To);
                }

                foreach (var reason in skipped)
                {
                    _logger.LogInformation("[dry run] skipping: {Reason}", reason);
                }

                _logger.LogInformation("[dry run] {Moves} move(s) planned across {Albums} album(s), {Skipped} skipped", moves.Count, plans.Count, skipped.Count);
                return new OrganizeReport { DryRun = true, Moves = moves, Skipped = skipped, Albums = plans.Count };
            }

            var failed = new List<string>();
            var applied = 0;
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                applied += Apply(plan, failed);
            }

            CleanUpVacatedDirectories(plans, failed);

            _logger.LogInformation("Applied {Applied} move(s) across {Albums} album(s); {Skipped} skipped, {Failed} failed", applied, plans.Count, skipped.Count, failed.Count);
            if (applied > 0)
            {
                _libraryManager.QueueLibraryScan();
            }

            return new OrganizeReport { Moves = moves, Skipped = skipped, Failed = failed, Applied = applied, Albums = plans.Count };
        }
        finally
        {
            _running.Release();
        }
    }

    private static bool PathExists(string path)
        => File.Exists(path) || Directory.Exists(path);

    private static bool IsAudio(string path)
        => AudioExtensions.Contains(Path.GetExtension(path));

    private static void DeleteEmptySubdirectories(string root)
    {
        foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private async Task<List<AlbumPlan>> PlanAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var options = _options();
        var planner = new OrganizePlanner(PathExists);
        var roots = _libraryManager.GetVirtualFolders()
            .SelectMany(folder => folder.Locations)
            .Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(root => root.Length)
            .ToList();

        var albums = _libraryManager
            .GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.MusicAlbum], Recursive = true })
            .OfType<MusicAlbum>()
            .Where(album => !string.IsNullOrEmpty(album.Path) && !string.IsNullOrEmpty(album.GetProviderId(ProviderKeys.Album)))
            .ToList();

        var plans = new List<AlbumPlan>();
        for (var i = 0; i < albums.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(100.0 * i / Math.Max(1, albums.Count));

            var album = albums[i];
            var plan = await PlanAlbumAsync(album, roots, planner, options, cancellationToken);
            if (plan is not null)
            {
                plans.Add(plan);
            }
        }

        progress?.Report(100);
        return plans;
    }

    private async Task<AlbumPlan?> PlanAlbumAsync(
        MusicAlbum album,
        IReadOnlyList<string> roots,
        OrganizePlanner planner,
        OrganizeOptions options,
        CancellationToken cancellationToken)
    {
        var root = roots.FirstOrDefault(candidate => album.Path.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        if (root is null)
        {
            _logger.LogWarning("Album {Name} at {Path} is not inside any library folder; skipping", album.Name, album.Path);
            return null;
        }

        var id = album.GetProviderId(ProviderKeys.Album)!;
        var storefront = album.GetProviderId(ProviderKeys.Storefront);
        var catalogAlbum = await _catalog.GetAlbumAsync(id, storefront, cancellationToken);
        if (catalogAlbum is null)
        {
            return new AlbumPlan(album.Name, [], [$"{album.Name}: Apple Music album {id} could not be fetched"], null, string.Empty);
        }

        var artistId = catalogAlbum.ArtistIds.Count > 0 ? catalogAlbum.ArtistIds[0] : null;
        if (artistId is null)
        {
            return new AlbumPlan(album.Name, [], [$"{album.Name}: Apple Music album {id} lists no artist"], null, string.Empty);
        }

        var artist = await _catalog.GetArtistAsync(artistId, catalogAlbum.Storefront, cancellationToken);
        var artistName = artist?.Attributes.Name ?? catalogAlbum.Attributes.ArtistName;
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return new AlbumPlan(album.Name, [], [$"{album.Name}: Apple Music artist {artistId} has no name"], null, string.Empty);
        }

        var tracks = album.Tracks
            .Where(track => !track.IsVirtualItem && !string.IsNullOrEmpty(track.Path))
            .Select(track => new TrackSnapshot(track.Path, track.GetProviderId(ProviderKeys.Song), track.ParentIndexNumber, track.IndexNumber, track.Name))
            .ToList();

        return planner.Plan(new AlbumSnapshot(album.Name, album.Path, root, tracks), catalogAlbum, artistName, artistId, options);
    }

    private int Apply(AlbumPlan plan, List<string> failed)
    {
        var applied = 0;
        string? albumDirectory = null;
        foreach (var move in plan.Moves)
        {
            try
            {
                var parent = Path.GetDirectoryName(move.To);
                if (parent is not null)
                {
                    Directory.CreateDirectory(parent);
                }

                if (move.Kind == MoveKind.Directory)
                {
                    Directory.Move(move.From, move.To);
                    albumDirectory = move.To;
                }
                else
                {
                    File.Move(move.From, move.To);
                    albumDirectory ??= Path.GetDirectoryName(move.To);
                }

                _logger.LogInformation("Moved {Kind} {From} -> {To}", move.Kind, move.From, move.To);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not move {From} -> {To}", move.From, move.To);
                failed.Add($"{move.From} -> {move.To}: {ex.Message}");

                // The remaining moves of this album assume this one happened.
                return applied;
            }
        }

        if (albumDirectory is not null && Directory.Exists(albumDirectory))
        {
            // Tracks pulled out of CD1/CD2 sub-directories leave them empty.
            DeleteEmptySubdirectories(albumDirectory);
        }

        return applied;
    }

    private void CleanUpVacatedDirectories(IReadOnlyList<AlbumPlan> plans, List<string> failed)
    {
        var vacated = plans
            .Where(plan => plan.VacatedDirectory is not null)
            .GroupBy(plan => plan.VacatedDirectory!, StringComparer.Ordinal);

        foreach (var group in vacated)
        {
            var directory = group.Key;
            if (!Directory.Exists(directory) || Directory.EnumerateDirectories(directory).Any())
            {
                continue;
            }

            var leftovers = Directory.GetFiles(directory);
            if (leftovers.Any(IsAudio))
            {
                continue;
            }

            // Only artist-level extras remain (artist.jpg, folder.jpg …). Carry
            // them over when every album went to the same place.
            var destinations = group.Select(plan => plan.TargetArtistDirectory).Distinct(StringComparer.Ordinal).ToList();
            if (leftovers.Length > 0 && destinations.Count == 1)
            {
                foreach (var file in leftovers)
                {
                    var target = Path.Combine(destinations[0], Path.GetFileName(file));
                    if (PathExists(target))
                    {
                        continue;
                    }

                    try
                    {
                        File.Move(file, target);
                        _logger.LogInformation("Moved {From} -> {To}", file, target);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogError(ex, "Could not move {From} -> {To}", file, target);
                        failed.Add($"{file} -> {target}: {ex.Message}");
                    }
                }
            }

            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                _logger.LogInformation("Removed the empty directory {Directory}", directory);
            }
        }
    }
}
