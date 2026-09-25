using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     A lock-free token-bucket rate limiter. Refills at <c>perSecond</c> tokens per second,
///     capped at <c>burst</c>. <see cref="TryAcquire"/> uses a compare-and-swap loop on a single
///     64-bit field so it is wait-free in the uncontended case and fair under contention.
/// </summary>
/// <remarks>
///     <para>The bucket state is encoded in a single <see cref="long"/>: the upper 32 bits hold the
///     last refill timestamp in milliseconds since process start (compared with wrap-around arithmetic, so a
///     process that runs longer than 49.7 days keeps limiting), the lower 32 bits hold the current token count
///     multiplied by 1024 (so we keep three decimal digits of precision without floats in the state).</para>
///     <para>2.9: the refill rate is kept as a fraction of a fixed-point unit per millisecond and the timestamp only
///     advances by the time that was actually converted into tokens. Before 2.9 the rate was rounded to a whole unit
///     per millisecond with a floor of one, so any rate below about 0.5/s (a per-minute limit, for example) refilled at
///     roughly one token per second, and the clock was read from system boot rather than process start.</para>
/// </remarks>
internal sealed class G9CTokenBucket
{
    private const int FixedShift = 10; // 1024 = 2^10
    private const int FixedFactor = 1 << FixedShift;

    private static readonly long Origin = Stopwatch.GetTimestamp();

    private readonly long _capacityFixed;
    private readonly double _refillPerMsFixed;
    private long _state; // (timestampMs << 32) | tokensFixed

    /// <summary>Milliseconds since the first bucket of this process was created (process-relative, never overflows).</summary>
    private static long ElapsedMs => (long)Stopwatch.GetElapsedTime(Origin).TotalMilliseconds;

    /// <summary>Initializes a new bucket.</summary>
    /// <param name="perSecond">Steady-state tokens per second.</param>
    /// <param name="burst">Maximum bucket depth.</param>
    public G9CTokenBucket(double perSecond, int burst)
    {
        if (perSecond <= 0 || double.IsNaN(perSecond) || double.IsInfinity(perSecond)) throw new ArgumentOutOfRangeException(nameof(perSecond));
        if (burst <= 0) throw new ArgumentOutOfRangeException(nameof(burst));

        _capacityFixed = Math.Min((long)burst * FixedFactor, 0xFFFF_FFFFL);
        _refillPerMsFixed = perSecond * FixedFactor / 1000.0;

        _state = Pack(ElapsedMs, _capacityFixed);
    }

    /// <summary>Attempts to acquire a single token. Returns true when granted.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire()
    {
        var now = ElapsedMs & 0xFFFF_FFFFL;
        SpinWait spin = default;

        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var (lastMs, tokensFixed) = Unpack(snapshot);
            var stampMs = lastMs;

            // Refill since last update. Wrap-around subtraction on the 32-bit timestamp.
            var elapsed = (now - lastMs) & 0xFFFF_FFFFL;
            if (elapsed > 0)
            {
                if (tokensFixed >= _capacityFixed)
                {
                    stampMs = now; // already full: nothing accrues while full
                }
                else
                {
                    var gained = (long)(elapsed * _refillPerMsFixed);
                    if (gained > 0)
                    {
                        tokensFixed += gained;
                        if (tokensFixed >= _capacityFixed)
                        {
                            tokensFixed = _capacityFixed;
                            stampMs = now;
                        }
                        else
                        {
                            // Advance only by the time that became tokens; the remainder keeps accruing, so a slow
                            // rate polled often still refills instead of losing its fraction on every call.
                            stampMs = (lastMs + Math.Min(elapsed, (long)(gained / _refillPerMsFixed))) & 0xFFFF_FFFFL;
                        }
                    }
                }
            }

            if (tokensFixed < FixedFactor)
            {
                // Nothing to take. Publish refill so future callers see the credit.
                var refilled = Pack(stampMs, tokensFixed);
                if (refilled == snapshot || Interlocked.CompareExchange(ref _state, refilled, snapshot) == snapshot)
                    return false;
            }
            else
            {
                var nextTokens = tokensFixed - FixedFactor;
                if (Interlocked.CompareExchange(ref _state, Pack(stampMs, nextTokens), snapshot) == snapshot)
                    return true;
            }

            spin.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(long timestampMs, long tokensFixed) =>
        ((timestampMs & 0xFFFF_FFFFL) << 32) | (tokensFixed & 0xFFFF_FFFFL);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (long timestampMs, long tokensFixed) Unpack(long state) =>
        ((state >> 32) & 0xFFFF_FFFFL, state & 0xFFFF_FFFFL);
}
