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

    /// <summary>Items yielded by streaming hub methods carrying [G9AttrTelemetry].</summary>
    public static readonly Counter<long> StreamItems =
        Meter.CreateCounter<long>("g9.signalr.stream_items", description: "Items yielded by streaming hub methods.");

    /// <summary>
    ///     How long a streaming hub method took to yield its FIRST item. A stream's total duration says
    ///     little on its own - it includes however long the consumer took to read - while time to first
    ///     item is what the caller waits for before anything happens.
    /// </summary>
    public static readonly Histogram<double> StreamFirstItemMs =
        Meter.CreateHistogram<double>("g9.signalr.stream_first_item_ms", unit: "ms", description: "Time to the first item of a streaming hub method.");

    /// <summary>End-to-end duration of a streaming hub method, from invocation to the end of enumeration.</summary>
    public static readonly Histogram<double> StreamDurationMs =
        Meter.CreateHistogram<double>("g9.signalr.stream_duration_ms", unit: "ms", description: "Full enumeration duration of a streaming hub method.");
}
