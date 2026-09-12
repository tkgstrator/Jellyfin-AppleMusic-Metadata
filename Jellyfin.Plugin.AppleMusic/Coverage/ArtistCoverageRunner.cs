using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AppleMusic.Catalog.Coverage;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Coverage;

/// <summary>
/// Gathers the library's artist names, hands them to
/// <see cref="ArtistCoverageProbe"/> and keeps the report. The decisions live
/// in the probe so they can be unit tested; this class only touches Jellyfin.
/// </summary>
public sealed class ArtistCoverageRunner : IDisposable
{
    private const int CheckpointEvery = 25;

    private readonly ILibraryManager _libraryManager;
    private readonly ArtistCoverageProbe _probe;
    private readonly ArtistCoverageStore _store;
    private readonly ILogger<ArtistCoverageRunner> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistCoverageRunner"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="probe">Probe doing the checking.</param>
    /// <param name="store">Where the report is kept.</param>
    /// <param name="logger">Logger.</param>
    public ArtistCoverageRunner(
        ILibraryManager libraryManager,
        ArtistCoverageProbe probe,
        ArtistCoverageStore store,
        ILogger<ArtistCoverageRunner> logger)
    {
        _libraryManager = libraryManager;
        _probe = probe;
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value indicating whether a run is in progress.
    /// </summary>
    public bool IsRunning => _running.CurrentCount == 0;

    /// <inheritdoc />
    public void Dispose()
        => _running.Dispose();

    /// <summary>
    /// Checks every artist name in the library and stores the report.
    /// </summary>
    /// <param name="progress">Receives the completed fraction in percent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">A run is already in progress.</exception>
    public async Task<ArtistCoverageReport> RunAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!await _running.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("An artist coverage run is already in progress.");
        }

        try
        {
            var names = CollectNames();
            _logger.LogInformation("Checking {Count} artist name(s) against Apple Music", names.Count);

            var report = await _probe.RunAsync(
                names,
                progress,
                partial => _store.SaveAsync(partial, cancellationToken),
                CheckpointEvery,
                cancellationToken);
            await _store.SaveAsync(report, cancellationToken);
            return report;
        }
        finally
        {
            _running.Release();
        }
    }

    /// <summary>
    /// Reads the latest report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report, or null when no run has happened yet.</returns>
    public Task<ArtistCoverageReport?> LoadReportAsync(CancellationToken cancellationToken)
        => _store.LoadAsync(cancellationToken);

    private List<string> CollectNames()
        => _libraryManager
            .GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.MusicArtist], Recursive = true })
            .OfType<MusicArtist>()
            .Select(artist => artist.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
}
