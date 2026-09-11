using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Coverage;

/// <summary>
/// Keeps the latest coverage report on disk, so the configuration page can
/// show it after the scheduled task has finished — or while it is running.
/// </summary>
public class ArtistCoverageStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistCoverageStore"/> class.
    /// </summary>
    /// <param name="path">File the report is written to.</param>
    public ArtistCoverageStore(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Gets the file the report is written to.
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Writes the report, replacing the previous one.
    /// </summary>
    /// <param name="report">Report to keep.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SaveAsync(ArtistCoverageReport report, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write beside and rename, so a reader never sees a half-written file.
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, report, Options, cancellationToken);
        }

        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// Reads the latest report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report, or null when none has been written.</returns>
    public async Task<ArtistCoverageReport?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<ArtistCoverageReport>(stream, Options, cancellationToken);
    }
}
