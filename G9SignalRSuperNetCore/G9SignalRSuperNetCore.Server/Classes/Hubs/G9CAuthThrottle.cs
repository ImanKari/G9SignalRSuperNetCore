using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

/// <summary>
///     Per-IP token bucket in front of the JWT authorize route (2.9). Registered as a singleton by
///     <c>AddSignalRSuperNetCoreJwt</c> and consulted by <see cref="G9GetJwtHub.Authorize"/> before the credential
///     check runs. Configured through <see cref="G9DtJwtAuthOptions"/>.
/// </summary>
/// <remarks>
///     A call without a known remote address (a non-TCP transport) is not throttled: sharing one bucket between all such
///     callers would let one of them lock every other out. Behind a reverse proxy, enable the forwarded-headers
///     middleware, or every client is throttled as the proxy.
/// </remarks>
public sealed partial class G9CAuthThrottle
{
    private const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _buckets = new(StringComparer.Ordinal);
    private readonly bool _enabled;
    private readonly double _perSecond;
    private readonly int _burst;
    private readonly TimeSpan _refillTime;

    /// <summary>Initializes the throttle from <see cref="G9DtJwtAuthOptions"/>.</summary>
    public G9CAuthThrottle(IOptions<G9DtJwtAuthOptions> options)
    {
        var value = options.Value;
        _enabled = value.ThrottleEnabled;
        _perSecond = Math.Max(1, value.AuthorizePerMinutePerIp) / 60.0;
        _burst = Math.Max(1, value.AuthorizeBurstPerIp);
        // How long an idle bucket takes to be full again; a bucket idle for longer can be dropped without loss.
        _refillTime = TimeSpan.FromSeconds(Math.Max(60, _burst / _perSecond));
    }

    /// <summary>Whether the throttle is on (<see cref="G9DtJwtAuthOptions.ThrottleEnabled"/>).</summary>
    public bool Enabled => _enabled;

    /// <summary>IP addresses currently holding a bucket.</summary>
    public int TrackedAddresses => _buckets.Count;

    /// <summary>Takes one call from the allowance of <paramref name="remoteIp"/>; returns <c>false</c> when it is exhausted.</summary>
    public bool TryAcquire(string? remoteIp)
    {
        if (!_enabled || string.IsNullOrEmpty(remoteIp)) return true;
        if (_buckets.Count > SweepThreshold) Sweep();

        var entry = _buckets.GetOrAdd(remoteIp, _ => new Entry(new G9CTokenBucket(_perSecond, _burst)));
        entry.Touch();
        return entry.Bucket.TryAcquire();
    }

    private void Sweep()
    {
        var cutoff = DateTime.UtcNow - _refillTime;
        foreach (var pair in _buckets)
            if (pair.Value.LastUsedUtc < cutoff) _buckets.TryRemove(pair);
    }

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Warning,
        Message = "G9 auth throttle rejected authorize: route={Route} ip={RemoteIp}")]
    internal static partial void LogThrottled(ILogger logger, string? route, string? remoteIp);

    private sealed class Entry(G9CTokenBucket bucket)
    {
        private long _lastUsedTicks = DateTime.UtcNow.Ticks;

        public G9CTokenBucket Bucket { get; } = bucket;

        public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

        public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
    }
}
