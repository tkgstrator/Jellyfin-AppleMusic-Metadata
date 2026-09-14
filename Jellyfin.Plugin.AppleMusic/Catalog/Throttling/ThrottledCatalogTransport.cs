using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AppleMusic.Catalog.Throttling;

/// <summary>
/// Serialises requests to the catalog, spaces them out and backs off when the
/// catalog answers 429.
/// </summary>
/// <remarks>
/// <para>
/// A library scan hands every track to the providers in parallel, so without
/// this the plugin fires as many searches at once as the server has cores.
/// amp-api limits the search endpoint per IP address, reports neither the
/// quota nor a Retry-After, and keeps refusing for a long time once tripped —
/// so the only safe policy is to never burst in the first place.
/// </para>
/// <para>
/// One request at a time, at least <see cref="ThrottleOptions.MinInterval"/>
/// apart. A 429 pauses requests of the same kind — search, or id lookup —
/// for a cooldown that doubles on each consecutive refusal; the refused
/// request is retried after the pause, up to
/// <see cref="ThrottleOptions.MaxAttempts"/> times. The two kinds are paused
/// separately because Apple has been observed refusing searches for hours
/// while still answering id lookups, and a tagged library needs only the
/// latter. Once the cooldown has hit
/// its ceiling the catalog is clearly refusing for a while, so lookups that
/// arrive during the pause fail immediately instead of queueing for minutes —
/// the scan then finishes without those items and a later refresh fills them
/// in. Failures propagate as <see cref="CatalogRateLimitedException"/> so the
/// cache never records them as "not found".
/// </para>
/// </remarks>
public sealed class ThrottledCatalogTransport : ICatalogTransport, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ICatalogTransport _inner;
    private readonly Func<ThrottleOptions> _options;
    private readonly ILogger<ThrottledCatalogTransport> _logger;
    private readonly TimeProvider _time;

    // All guarded by _gate.
    private readonly Pause _searchPause = new();
    private readonly Pause _lookupPause = new();
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottledCatalogTransport"/> class.
    /// </summary>
    /// <param name="inner">Transport that performs the actual request.</param>
    /// <param name="options">
    /// Supplies the current options. A delegate rather than a value, because the
    /// user can change the settings while the server is running.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">Clock; defaults to the system clock.</param>
    public ThrottledCatalogTransport(
        ICatalogTransport inner,
        Func<ThrottleOptions> options,
        ILogger<ThrottledCatalogTransport> logger,
        TimeProvider? time = null)
    {
        _inner = inner;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets a value indicating whether the transport is currently pausing
    /// because the catalog refused a request.
    /// </summary>
    public bool IsCoolingDown => IsPaused(_searchPause) || IsPaused(_lookupPause);

    /// <inheritdoc />
    public void Dispose()
        => _gate.Dispose();

    /// <inheritdoc />
    public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var options = _options();
        var kind = IsSearch(relativeUrl) ? "search" : "lookup";
        var pause = IsSearch(relativeUrl) ? _searchPause : _lookupPause;

        for (var attempt = 1; ; attempt++)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await WaitForSlotAsync(options, pause, relativeUrl, cancellationToken);

                try
                {
                    var body = await _inner.GetAsync(relativeUrl, cancellationToken);
                    pause.Cooldown = TimeSpan.Zero;
                    _nextSlot = _time.GetUtcNow() + options.MinInterval;
                    return body;
                }
                catch (CatalogRateLimitedException)
                {
                    pause.Cooldown = pause.Cooldown == TimeSpan.Zero
                        ? options.InitialCooldown
                        : Min(pause.Cooldown + pause.Cooldown, options.MaxCooldown);
                    pause.Until = _time.GetUtcNow() + pause.Cooldown;

                    if (attempt >= options.MaxAttempts)
                    {
                        _logger.LogWarning(
                            "Apple Music kept refusing {Url} after {Attempts} attempts; giving up on it and pausing {Kind} requests for {Cooldown}",
                            relativeUrl,
                            attempt,
                            kind,
                            pause.Cooldown);
                        throw;
                    }

                    _logger.LogWarning(
                        "Apple Music rate limited {Url}; pausing {Kind} requests for {Cooldown} before attempt {Next}",
                        relativeUrl,
                        kind,
                        pause.Cooldown,
                        attempt + 1);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static bool IsSearch(string relativeUrl)
        => relativeUrl.Contains("/search?", StringComparison.Ordinal);

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
        => left < right ? left : right;

    private bool IsPaused(Pause pause)
        => pause.Cooldown > TimeSpan.Zero && _time.GetUtcNow() < pause.Until;

    // Called while holding _gate, so everybody else queues behind the wait.
    private async Task WaitForSlotAsync(ThrottleOptions options, Pause pause, string relativeUrl, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var pauseWait = pause.Until - now;
        if (pauseWait > TimeSpan.Zero && pause.Cooldown >= options.MaxCooldown)
        {
            // The catalog has been refusing this kind of request for a while.
            // Do not make the scan queue up for minutes per item; fail fast
            // until the pause has elapsed and one request can probe again.
            _logger.LogDebug("Skipping {Url}: Apple Music is still refusing such requests for another {Wait}", relativeUrl, pauseWait);
            throw new CatalogRateLimitedException();
        }

        var wait = Max(_nextSlot - now, pauseWait);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, _time, cancellationToken);
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
        => left > right ? left : right;

    private sealed class Pause
    {
        public TimeSpan Cooldown { get; set; }

        public DateTimeOffset Until { get; set; } = DateTimeOffset.MinValue;
    }
}
