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
/// apart. A 429 pauses everything for a cooldown that doubles on each
/// consecutive refusal; the refused lookup is retried after the pause, up to
/// <see cref="ThrottleOptions.MaxAttempts"/> times. Once the cooldown has hit
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

    // Both guarded by _gate.
    private DateTimeOffset _nextAllowed = DateTimeOffset.MinValue;
    private TimeSpan _cooldown = TimeSpan.Zero;

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
    public bool IsCoolingDown => _cooldown > TimeSpan.Zero && _time.GetUtcNow() < _nextAllowed;

    /// <inheritdoc />
    public void Dispose()
        => _gate.Dispose();

    /// <inheritdoc />
    public async Task<string?> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var options = _options();

        for (var attempt = 1; ; attempt++)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await WaitForSlotAsync(options, relativeUrl, cancellationToken);

                try
                {
                    var body = await _inner.GetAsync(relativeUrl, cancellationToken);
                    _cooldown = TimeSpan.Zero;
                    _nextAllowed = _time.GetUtcNow() + options.MinInterval;
                    return body;
                }
                catch (CatalogRateLimitedException)
                {
                    _cooldown = _cooldown == TimeSpan.Zero
                        ? options.InitialCooldown
                        : Min(_cooldown + _cooldown, options.MaxCooldown);
                    _nextAllowed = _time.GetUtcNow() + _cooldown;

                    if (attempt >= options.MaxAttempts)
                    {
                        _logger.LogWarning(
                            "Apple Music kept refusing {Url} after {Attempts} attempts; giving up on this lookup and pausing for {Cooldown}",
                            relativeUrl,
                            attempt,
                            _cooldown);
                        throw;
                    }

                    _logger.LogWarning(
                        "Apple Music rate limited {Url}; pausing all requests for {Cooldown} before attempt {Next}",
                        relativeUrl,
                        _cooldown,
                        attempt + 1);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
        => left < right ? left : right;

    // Called while holding _gate, so everybody else queues behind the wait.
    private async Task WaitForSlotAsync(ThrottleOptions options, string relativeUrl, CancellationToken cancellationToken)
    {
        var wait = _nextAllowed - _time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        if (_cooldown >= options.MaxCooldown)
        {
            // The catalog has been refusing us for a while. Do not make the
            // scan queue up for minutes per item; fail fast until the pause
            // has elapsed and one request can probe again.
            _logger.LogDebug("Skipping {Url}: Apple Music is still refusing requests for another {Wait}", relativeUrl, wait);
            throw new CatalogRateLimitedException();
        }

        await Task.Delay(wait, _time, cancellationToken);
    }
}
