namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Marks a hub method (or hub class) for OpenTelemetry tracing through the
///     <c>G9SignalRSuperNetCore</c> <see cref="System.Diagnostics.ActivitySource"/>.
/// </summary>
/// <remarks>
///     <para>The filter starts an <see cref="System.Diagnostics.Activity"/> named
///     <c>{HubName}.{MethodName}</c> (or the <see cref="Name"/> override) around each invocation,
///     tagging the connection id, user id, and the outcome (Ok / Faulted / Cancelled).</para>
///     <para>When the attribute is absent, no telemetry is recorded (zero cost).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrTelemetryAttribute : Attribute
{
    /// <summary>Optional override for the activity name.</summary>
    public string? Name { get; }

    /// <summary>
    ///     Fraction of invocations that get a span (2.9), from <c>0.0</c> (none) to <c>1.0</c> (all, the default).
    ///     Values outside that range are clamped. The decision is a uniform random draw per invocation, so a hot
    ///     method can be traced at, say, <c>SampleRate = 0.01</c> without paying for a span on every call.
    ///     Unsampled calls skip the span entirely; the policy metrics are unaffected.
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>Initializes a new telemetry attribute.</summary>
    /// <param name="name">Optional override for the activity name.</param>
    public G9AttrTelemetryAttribute(string? name = null) => Name = name;
}
