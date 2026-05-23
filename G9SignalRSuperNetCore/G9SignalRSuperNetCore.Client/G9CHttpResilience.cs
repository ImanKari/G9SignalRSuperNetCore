using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     Composable HTTP resilience for the SignalR transport. SignalR opens its connection with
///     an <see cref="HttpClient"/> for the negotiate request and falls back to long-polling /
///     server-sent events through the same client. Wrapping the inner handler in a Polly v8
///     <see cref="ResiliencePipeline{TResult}"/> hardens the negotiate phase against transient
///     DNS / TLS / reverse-proxy failures.
/// </summary>
/// <remarks>
///     <para>WebSocket frames bypass <see cref="HttpClient"/> after the upgrade succeeds, so this
///     resilience layer only affects connection establishment + transport fallback. Steady-state
///     reconnects are still owned by <see cref="G9CClientReconnectPolicy"/>.</para>
/// </remarks>
public static class G9CHttpResilience
{
    /// <summary>
    ///     Returns a default Polly v8 pipeline that handles transient network faults: 5 retries
    ///     with exponential backoff (200 ms base, factor 2, ±15% jitter, 30 s cap), 10 s per-attempt
    ///     timeout. Used by <see cref="ApplyDefault"/>.
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> CreateDefaultPipeline()
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(static r =>
                        r.StatusCode == HttpStatusCode.RequestTimeout ||
                        r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                        r.StatusCode == HttpStatusCode.BadGateway ||
                        r.StatusCode == HttpStatusCode.GatewayTimeout)
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

    /// <summary>
    ///     Wraps the negotiate <see cref="HttpClient"/>'s message handler with the supplied
    ///     <paramref name="pipeline"/>. Apply this from inside a
    ///     <c>configureHttpConnection</c> callback when building the client:
    ///     <code>
    ///         new HubConnectionBuilder()
    ///             .WithUrl(url, opts =&gt; G9CHttpResilience.Apply(opts, G9CHttpResilience.CreateDefaultPipeline()))
    ///             .Build();
    ///     </code>
    /// </summary>
    public static void Apply(HttpConnectionOptions options, ResiliencePipeline<HttpResponseMessage> pipeline)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pipeline);

        var existing = options.HttpMessageHandlerFactory;
        options.HttpMessageHandlerFactory = inner =>
        {
            var baseHandler = existing is null ? inner : existing(inner);
            return new ResilienceHandler(pipeline) { InnerHandler = baseHandler };
        };
    }

    /// <summary>Convenience for <see cref="Apply"/> with the library's default pipeline.</summary>
    public static void ApplyDefault(HttpConnectionOptions options) => Apply(options, CreateDefaultPipeline());
}
