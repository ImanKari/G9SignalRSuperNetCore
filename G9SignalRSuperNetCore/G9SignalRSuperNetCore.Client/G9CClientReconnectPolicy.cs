using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     Tunable <see cref="IRetryPolicy"/> for <c>HubConnectionBuilder.WithAutomaticReconnect()</c>.
///     Implements exponential backoff with jitter and a configurable maximum elapsed time so a
///     long outage doesn't keep the client retrying forever.
/// </summary>
/// <remarks>
///     <para>The default SignalR client retries at 0, 2, 10, and 30 seconds and then gives up.
///     This policy keeps retrying with capped exponential backoff (default base 200 ms, factor 2,
///     max 30 s, max elapsed 5 min) until the elapsed budget is exhausted.</para>
///     <para>For consumers that want full control over the retry curve (circuit breakers,
///     custom backoff, telemetry hooks), use <see cref="FromDelegate"/> to plug in a
///     <see cref="Func{T, TResult}"/> that returns the next delay.</para>
///     <para>Use it like:</para>
///     <code>
///         var conn = new HubConnectionBuilder()
///             .WithUrl(url)
///             .WithAutomaticReconnect(new G9CClientReconnectPolicy())
///             .Build();
///     </code>
/// </remarks>
public sealed class G9CClientReconnectPolicy : IRetryPolicy
{
    private readonly Func<RetryContext, TimeSpan?> _delayer;

    /// <summary>Initializes the policy with sensible defaults.</summary>
    public G9CClientReconnectPolicy() : this(
        baseDelay: TimeSpan.FromMilliseconds(200),
        factor: 2.0,
        maxDelay: TimeSpan.FromSeconds(30),
        maxElapsed: TimeSpan.FromMinutes(5))
    { }

    /// <summary>Initializes the policy with custom parameters.</summary>
    /// <param name="baseDelay">First retry delay.</param>
    /// <param name="factor">Multiplier applied to the delay between consecutive retries.</param>
    /// <param name="maxDelay">Upper bound on a single delay.</param>
    /// <param name="maxElapsed">Upper bound on total reconnection time before giving up. Use
    /// <see cref="Timeout.InfiniteTimeSpan"/> to retry forever.</param>
    public G9CClientReconnectPolicy(TimeSpan baseDelay, double factor, TimeSpan maxDelay, TimeSpan maxElapsed)
    {
        if (baseDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(baseDelay));
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
        if (maxDelay < baseDelay) throw new ArgumentOutOfRangeException(nameof(maxDelay));

        var jitter = new Random();
        _delayer = ctx =>
        {
            if (maxElapsed != Timeout.InfiniteTimeSpan && ctx.ElapsedTime >= maxElapsed)
                return null;

            var raw = baseDelay.TotalMilliseconds * Math.Pow(factor, ctx.PreviousRetryCount);
            var clamped = Math.Min(raw, maxDelay.TotalMilliseconds);
            // ±15% jitter so coordinated failures don't cause a thundering herd.
            var jitterFactor = 0.85 + jitter.NextDouble() * 0.30;
            return TimeSpan.FromMilliseconds(clamped * jitterFactor);
        };
    }

    private G9CClientReconnectPolicy(Func<RetryContext, TimeSpan?> delayer) => _delayer = delayer;

    /// <summary>
    ///     Builds a policy that delegates the delay decision to a custom function. Useful when
    ///     you want to express the retry curve in your own code (e.g. consult a circuit-breaker,
    ///     read telemetry, or vary the schedule by error type).
    /// </summary>
    /// <param name="next">Returns the next retry delay or null to give up.</param>
    public static G9CClientReconnectPolicy FromDelegate(Func<RetryContext, TimeSpan?> next)
    {
        if (next is null) throw new ArgumentNullException(nameof(next));
        return new G9CClientReconnectPolicy(next);
    }

    /// <inheritdoc />
    public TimeSpan? NextRetryDelay(RetryContext retryContext) => _delayer(retryContext);
}
