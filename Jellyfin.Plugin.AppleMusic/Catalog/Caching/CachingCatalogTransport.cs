using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Caching;

/// <summary>
/// Wraps a transport so identical lookups are served from the cache.
/// </summary>
/// <remarks>
/// Two distinct savings. The cache proper stops a URL being fetched twice
/// across the life of the library. Separately, in-flight requests are shared:
/// a library scan processes an album's tracks in parallel, so the same album
/// lookup would otherwise be issued once per track, simultaneously, with
/// nothing in the cache yet to stop it.
/// </remarks>
public class CachingCatalogTransport : ICatalogTransport
{
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new(StringComparer.Ordinal);
    private readonly ICatalogTransport _inner;
    private readonly ICatalogCache _cache;
    private readonly ILogger<CachingCatalogTransport> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingCatalogTransport"/> class.
    /// </summary>
    /// <param name="inner">Transport that performs the actual request.</param>
    /// <param name="cache">Response cache.</param>
    /// <param name="logger">Logger.</param>
    public CachingCatalogTransport(
        ICatalogTransport inner,
        ICatalogCache cache,
        ILogger<CachingCatalogTransport> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        if (_cache.TryGet(relativeUrl, out var cached))
        {
            _logger.LogDebug("Cache hit for {Url}", relativeUrl);
            return cached;
        }

        // Publish the placeholder BEFORE fetching. Registering the task the
        // fetch returns instead would let a synchronously-completing fetch
        // remove its entry before GetOrAdd had inserted it, leaving a finished
        // task in the map forever.
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _inFlight.GetOrAdd(relativeUrl, completion.Task);
        if (!ReferenceEquals(existing, completion.Task))
        {
            _logger.LogDebug("Joining an in-flight lookup for {Url}", relativeUrl);
            return await existing;
        }

        try
        {
            var body = await _inner.GetAsync(relativeUrl, cancellationToken);
            _cache.Set(relativeUrl, body);
            completion.SetResult(body);
            return body;
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
            throw;
        }
        finally
        {
            _inFlight.TryRemove(relativeUrl, out _);
        }
    }
}
