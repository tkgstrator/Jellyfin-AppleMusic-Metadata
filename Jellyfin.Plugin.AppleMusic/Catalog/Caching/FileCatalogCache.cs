using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// Keeps catalog responses in memory and mirrors them to a JSON file so they
/// survive a server restart.
/// </summary>
/// <remarks>
/// A plain file rather than a database: Jellyfin ships no SQLite assembly for
/// plugins to reuse, so using one would mean shipping native libraries per
/// platform and risking a clash with the SQLite the server has already loaded.
/// At the scale involved — a few thousand entries for a large library — a
/// single file read at startup and written back on a timer is enough.
/// </remarks>
public sealed class FileCatalogCache : ICatalogCache, IDisposable
{
    private static readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly Func<CatalogCacheOptions> _options;
    private readonly ILogger<FileCatalogCache> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly Timer _flushTimer;

    private int _isDirty;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileCatalogCache"/> class.
    /// </summary>
    /// <param name="path">File the cache is persisted to.</param>
    /// <param name="options">Supplies the current cache options.</param>
    /// <param name="logger">Logger.</param>
    public FileCatalogCache(string path, Func<CatalogCacheOptions> options, ILogger<FileCatalogCache> logger)
    {
        _path = path;
        _options = options;
        _logger = logger;

        Load();
        _flushTimer = new Timer(_ => FlushIfDirty(), null, _flushInterval, _flushInterval);
    }

    /// <summary>
    /// Gets the number of entries currently held.
    /// </summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public bool TryGet(string key, out string? value)
    {
        value = null;
        if (!_options().Enabled)
        {
            return false;
        }

        if (!_entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (!entry.IsLive)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        value = entry.Body;
        return true;
    }

    /// <inheritdoc />
    public void Set(string key, string? value)
    {
        var options = _options();
        if (!options.Enabled)
        {
            return;
        }

        var lifetime = value is null ? options.NegativeLifetime : options.Lifetime;
        _entries[key] = new CacheEntry
        {
            Body = value,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
        };

        Interlocked.Exchange(ref _isDirty, 1);
        Trim(options.MaxEntries);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _entries.Clear();
        Interlocked.Exchange(ref _isDirty, 1);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isDirty, 0) == 0)
        {
            return;
        }

        var snapshot = _entries
            .Where(pair => pair.Value.IsLive)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write beside the target and move into place, so a crash midway
            // cannot leave a half-written cache behind.
            var temporary = _path + ".tmp";
            var stream = File.Create(temporary);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
            }

            File.Move(temporary, _path, true);
            _logger.LogDebug("Persisted {Count} catalog cache entries", snapshot.Count);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not persist the catalog cache to {Path}", _path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Not allowed to write the catalog cache to {Path}", _path);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _flushTimer.Dispose();
        FlushIfDirty();
        _fileLock.Dispose();
    }

    private void FlushIfDirty()
    {
        try
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flushing the catalog cache failed");
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_path);
            var stored = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(stream);
            if (stored is null)
            {
                return;
            }

            foreach (var pair in stored.Where(pair => pair.Value.IsLive))
            {
                _entries[pair.Key] = pair.Value;
            }

            _logger.LogInformation("Loaded {Count} catalog cache entries from {Path}", _entries.Count, _path);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged cache is not worth failing over; start empty.
            _logger.LogWarning(ex, "Could not read the catalog cache at {Path}, starting empty", _path);
        }
    }

    private void Trim(int maxEntries)
    {
        if (maxEntries <= 0 || _entries.Count <= maxEntries)
        {
            return;
        }

        // Drop whatever expires soonest, which also clears out dead entries.
        var doomed = _entries
            .OrderBy(pair => pair.Value.ExpiresAt)
            .Take(_entries.Count - maxEntries)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in doomed)
        {
            _entries.TryRemove(key, out _);
        }

        _logger.LogDebug("Trimmed {Count} catalog cache entries", doomed.Count);
    }
}
