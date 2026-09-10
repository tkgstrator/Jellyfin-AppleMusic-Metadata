using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// Two-tier response cache: a byte-budgeted LRU in memory, backed by one file
/// per entry on disk.
/// </summary>
/// <remarks>
/// Sized for large libraries. Holding every response in memory is not an
/// option — a 50,000 track library would produce roughly 1.5 GB of search
/// responses, and .NET strings are UTF-16, so about double that in RAM. Nor can
/// the whole cache live in one file that gets rewritten periodically.
/// <para>
/// So: memory is capped by bytes and evicts least-recently-used entries, and
/// the disk tier writes each entry to its own file under a two-character shard
/// directory, which keeps writes O(1) and avoids one enormous directory.
/// Entries above <see cref="CatalogCacheOptions.MaxPersistedEntryBytes"/> stay
/// in memory only, which in practice keeps large search responses off disk
/// while preserving the small id lookups that actually repeat.
/// </para>
/// </remarks>
public sealed class CatalogCache : ICatalogCache
{
    private readonly Dictionary<string, LinkedListNode<MemoryEntry>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<MemoryEntry> _recency = new();
    private readonly Lock _gate = new();

    private readonly string _root;
    private readonly Func<CatalogCacheOptions> _options;
    private readonly ILogger<CatalogCache> _logger;

    private long _memoryBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogCache"/> class.
    /// </summary>
    /// <param name="root">Directory the disk tier lives in.</param>
    /// <param name="options">Supplies the current cache options.</param>
    /// <param name="logger">Logger.</param>
    public CatalogCache(string root, Func<CatalogCacheOptions> options, ILogger<CatalogCache> logger)
    {
        _root = root;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Gets the number of entries currently held in memory.
    /// </summary>
    public int MemoryCount
    {
        get
        {
            lock (_gate)
            {
                return _index.Count;
            }
        }
    }

    /// <summary>
    /// Gets the approximate memory currently used by cached bodies, in bytes.
    /// </summary>
    public long MemoryBytes
    {
        get
        {
            lock (_gate)
            {
                return _memoryBytes;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<CacheHit?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!_options().Enabled)
        {
            return null;
        }

        if (TryGetFromMemory(key, out var hit))
        {
            return hit;
        }

        var entry = await ReadFromDiskAsync(key, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        // Promote so a repeat lookup does not touch the disk again.
        StoreInMemory(key, entry);
        return new CacheHit(entry.Body);
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var options = _options();
        if (!options.Enabled)
        {
            return;
        }

        var lifetime = value is null ? options.NegativeLifetime : options.Lifetime;
        var entry = new CacheEntry
        {
            Body = value,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
        };

        StoreInMemory(key, entry);

        if (SizeOf(value) <= options.MaxPersistedEntryBytes)
        {
            await WriteToDiskAsync(key, entry, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _recency.Clear();
            _memoryBytes = 0;
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clear the catalog cache directory {Path}", _root);
        }
    }

    /// <summary>
    /// Deletes expired files from the disk tier.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files removed.</returns>
    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await ReadFileAsync(file, cancellationToken);
            if (entry is null || !entry.IsLive)
            {
                TryDelete(file);
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Removed {Count} expired catalog cache files", removed);
        }

        return removed;
    }

    private static long SizeOf(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private bool TryGetFromMemory(string key, out CacheHit? hit)
    {
        hit = null;
        lock (_gate)
        {
            if (!_index.TryGetValue(key, out var node))
            {
                return false;
            }

            if (!node.Value.Entry.IsLive)
            {
                Remove(node);
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            hit = new CacheHit(node.Value.Entry.Body);
            return true;
        }
    }

    private void StoreInMemory(string key, CacheEntry entry)
    {
        var size = SizeOf(entry.Body);
        lock (_gate)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                Remove(existing);
            }

            var node = _recency.AddFirst(new MemoryEntry(key, entry, size));
            _index[key] = node;
            _memoryBytes += size;

            var budget = _options().MaxMemoryBytes;
            while (_memoryBytes > budget && _recency.Last is { } last)
            {
                Remove(last);
            }
        }
    }

    private void Remove(LinkedListNode<MemoryEntry> node)
    {
        _recency.Remove(node);
        _index.Remove(node.Value.Key);
        _memoryBytes -= node.Value.Size;
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
            .ToLowerInvariant();

        // Shard by the first two characters: one directory per key would put
        // tens of thousands of files side by side.
        return Path.Combine(_root, hash[..2], hash + ".json");
    }

    private async ValueTask<CacheEntry?> ReadFromDiskAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        var entry = await ReadFileAsync(path, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        if (!entry.IsLive)
        {
            TryDelete(path);
            return null;
        }

        return entry;
    }

    private async ValueTask<CacheEntry?> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(path);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonSerializer.DeserializeAsync<CacheEntry>(stream, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged entry is not worth failing over; treat it as a miss.
            _logger.LogDebug(ex, "Discarding unreadable catalog cache file {Path}", path);
            TryDelete(path);
            return null;
        }
    }

    private async ValueTask WriteToDiskAsync(string key, CacheEntry entry, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write beside the target and move into place, so an interrupted
            // write cannot leave a truncated entry behind.
            var temporary = path + "." + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            var stream = File.Create(temporary);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken);
            }

            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist a catalog cache entry to {Path}", path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete the catalog cache file {Path}", path);
        }
    }

    private sealed class MemoryEntry
    {
        public MemoryEntry(string key, CacheEntry entry, long size)
        {
            Key = key;
            Entry = entry;
            Size = size;
        }

        public string Key { get; }

        public CacheEntry Entry { get; }

        public long Size { get; }
    }
}
