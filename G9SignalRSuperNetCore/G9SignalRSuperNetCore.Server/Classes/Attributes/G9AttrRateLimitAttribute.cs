namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Applies a per-connection token-bucket rate limit to a single hub method.
///     When the limit is exceeded the call is rejected with
///     <see cref="Errors.G9CErrorCodes.RateLimited"/> and a metric is emitted.
/// </summary>
/// <remarks>
///     <para>The limiter is enforced by the <see cref="Filters.G9CHubFilter"/> hub filter,
///     which is registered automatically by <c>AddSignalRSuperNetCoreCore()</c>.</para>
///     <para>The limiter is per-(connection, method) so two methods on the same connection have
///     independent buckets. State is in-process; cluster-wide rate limiting will arrive in
///     Bundle 5 via the Redis package.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrRateLimitAttribute : Attribute
{
    /// <summary>Steady-state requests permitted per second (positive).</summary>
    public double PerSecond { get; }

    /// <summary>Maximum burst depth (positive). When unset, defaults to <see cref="PerSecond"/>.</summary>
    public int Burst { get; }

    /// <summary>Initializes a new rate-limit attribute.</summary>
    /// <param name="perSecond">Steady-state requests per second (must be greater than zero).</param>
    /// <param name="burst">Maximum burst depth (defaults to <paramref name="perSecond"/> rounded up).</param>
    public G9AttrRateLimitAttribute(double perSecond, int burst = 0)
    {
        if (perSecond <= 0) throw new ArgumentOutOfRangeException(nameof(perSecond));
        PerSecond = perSecond;
        Burst = burst > 0 ? burst : (int)Math.Ceiling(perSecond);
    }
}
