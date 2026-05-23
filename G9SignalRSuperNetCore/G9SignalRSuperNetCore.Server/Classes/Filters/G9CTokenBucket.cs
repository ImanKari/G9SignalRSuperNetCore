using System.Runtime.CompilerServices;
using System.Threading;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     A lock-free token-bucket rate limiter. Refills at <c>perSecond</c> tokens per second,
///     capped at <c>burst</c>. <see cref="TryAcquire"/> uses a compare-and-swap loop on a single
///     64-bit field so it is wait-free in the uncontended case and fair under contention.
/// </summary>
/// <remarks>
///     The bucket state is encoded in a single <see cref="long"/>: the upper 32 bits hold the
///     last refill timestamp in milliseconds since process start, the lower 32 bits hold the
///     current token count multiplied by 1024 (so we keep three decimal digits of precision
///     without floats in the hot path).
/// </remarks>
internal sealed class G9CTokenBucket
{
    private const int FixedShift = 10; // 1024 = 2^10
    private const int FixedFactor = 1 << FixedShift;

    private readonly long _capacityFixed;
    private readonly long _refillPerMsFixed;
    private long _state; // (timestampMs << 32) | tokensFixed

    private static long ElapsedMs => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Initializes a new bucket.</summary>
    /// <param name="perSecond">Steady-state tokens per second.</param>
    /// <param name="burst">Maximum bucket depth.</param>
    public G9CTokenBucket(double perSecond, int burst)
    {
        if (perSecond <= 0) throw new ArgumentOutOfRangeException(nameof(perSecond));
        if (burst <= 0) throw new ArgumentOutOfRangeException(nameof(burst));

        _capacityFixed = (long)burst * FixedFactor;
        _refillPerMsFixed = (long)Math.Round(perSecond * FixedFactor / 1000.0);
        if (_refillPerMsFixed <= 0) _refillPerMsFixed = 1; // avoid divide-by-zero on extreme inputs

        _state = Pack(ElapsedMs, _capacityFixed);
    }

    /// <summary>Attempts to acquire a single token. Returns true when granted.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire()
    {
        var now = ElapsedMs;
        SpinWait spin = default;

        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var (lastMs, tokensFixed) = Unpack(snapshot);

            // Refill since last update.
            var elapsed = now - lastMs;
            if (elapsed > 0)
            {
                tokensFixed += elapsed * _refillPerMsFixed;
                if (tokensFixed > _capacityFixed) tokensFixed = _capacityFixed;
            }

            if (tokensFixed < FixedFactor)
            {
                // Nothing to take. Publish refill so future callers see the credit.
                if (Interlocked.CompareExchange(ref _state, Pack(now, tokensFixed), snapshot) == snapshot)
                    return false;
            }
            else
            {
                var nextTokens = tokensFixed - FixedFactor;
                if (Interlocked.CompareExchange(ref _state, Pack(now, nextTokens), snapshot) == snapshot)
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
