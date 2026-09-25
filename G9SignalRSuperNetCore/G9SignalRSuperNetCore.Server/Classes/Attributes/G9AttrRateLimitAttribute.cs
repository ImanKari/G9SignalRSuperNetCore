namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Applies a token-bucket rate limit to a single hub method. When the limit is exceeded the call is
///     rejected with <see cref="Errors.G9CErrorCodes.RateLimited"/> and a metric is emitted.
/// </summary>
/// <remarks>
///     <para>The limiter is enforced by the <see cref="Filters.G9CHubFilter"/> hub filter,
///     which is registered automatically by <c>AddSignalRSuperNetCoreCore()</c>.</para>
///     <para>Buckets are always per method name, so two methods have independent buckets. By default they are also per
///     connection; set <see cref="Scope"/> (2.9) to share one bucket between every connection of a user
///     (<see cref="G9ERateLimitScope.User"/>) or of a remote IP address (<see cref="G9ERateLimitScope.Ip"/>):</para>
///     <code>
///         [G9AttrRateLimit(perSecond: 1, burst: 5, Scope = G9ERateLimitScope.User)]
///         public Task SendInvite(string email) =&gt; ...;
///     </code>
///     <para>State is in-process: in a scaled-out deployment each node enforces its own allowance.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrRateLimitAttribute : Attribute
{
    /// <summary>Steady-state requests permitted per second (positive).</summary>
    public double PerSecond { get; }

    /// <summary>Maximum burst depth (positive). When unset, defaults to <see cref="PerSecond"/>.</summary>
    public int Burst { get; }

    /// <summary>
    ///     Who shares a bucket (2.9): the connection (default, the pre-2.9 behaviour), the authenticated user, or the
    ///     remote IP address. Set it as a named argument: <c>[G9AttrRateLimit(1, 5, Scope = G9ERateLimitScope.User)]</c>.
    /// </summary>
    public G9ERateLimitScope Scope { get; set; } = G9ERateLimitScope.Connection;

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
