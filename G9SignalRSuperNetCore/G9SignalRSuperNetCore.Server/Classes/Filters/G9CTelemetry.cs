using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     Process-wide observability surface for the G9 hub filter pipeline.
///     Hosts a single <see cref="ActivitySource"/> for tracing and a single <see cref="Meter"/>
///     for metrics so consumers can subscribe through OpenTelemetry or
///     <see cref="System.Diagnostics.Metrics.MeterListener"/>.
/// </summary>
public static class G9CTelemetry
{
    /// <summary>The activity source name. Subscribe via <c>OpenTelemetry.AddSource(G9CTelemetry.SourceName)</c>.</summary>
    public const string SourceName = "G9SignalRSuperNetCore";

    /// <summary>
    ///     The library version reported on emitted spans and metrics. Read from
    ///     <see cref="AssemblyInformationalVersionAttribute"/> so it stays in sync with the
    ///     NuGet package version automatically — no manual bookkeeping per release.
    /// </summary>
    public static readonly string Version =
        typeof(G9CTelemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?.Split('+')[0] // strip "+gitsha" suffix produced by SourceLink
        ?? typeof(G9CTelemetry).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>The shared activity source.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>The shared meter for hub metrics.</summary>
    public static readonly Meter Meter = new(SourceName, Version);

    /// <summary>Counts hub method invocations rejected by the rate limiter.</summary>
    public static readonly Counter<long> RateLimitedInvocations =
        Meter.CreateCounter<long>("g9.signalr.rate_limited_invocations", description: "Hub invocations rejected by [G9AttrRateLimit].");

    /// <summary>Counts connections rejected by the connection-limit attribute.</summary>
    public static readonly Counter<long> ConnectionLimitRejections =
        Meter.CreateCounter<long>("g9.signalr.connection_limit_rejections", description: "Connections rejected by [G9AttrConnectionLimit].");

    /// <summary>Counts hub invocations rejected because authorization claims/roles are missing.</summary>
    public static readonly Counter<long> AuthorizationRejections =
        Meter.CreateCounter<long>("g9.signalr.authorization_rejections", description: "Hub invocations rejected by [G9AttrRequireRole]/[G9AttrRequireClaim].");
}
