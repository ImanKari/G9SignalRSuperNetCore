using System.Diagnostics;
using System.Runtime.CompilerServices;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Filters;

namespace G9SignalRSuperNetCore.Server.Classes.Streaming;

/// <summary>
///     Measures a streaming hub method across its whole enumeration.
/// </summary>
/// <remarks>
///     <see cref="G9AttrTelemetryAttribute"/> on a streaming method can only time the invocation: the
///     method returns as soon as its iterator exists, long before the first item, and the filter cannot
///     wrap the stream because it would have to build the generic type at runtime, which is not
///     AOT-safe. Wrapping it here instead keeps the item type at the call site, so this is AOT- and
///     trim-safe, and records what actually matters for a stream: time to first item, item count, full
///     duration, and how it ended.
/// </remarks>
public static class G9CStreamTelemetry
{
    /// <summary>
    ///     Wraps <paramref name="source"/> so the span and metrics cover the enumeration:
    ///     <c>return G9CStreamTelemetry.Track(Produce(ct), "Hub.Method", ct);</c>
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="source">The stream to measure.</param>
    /// <param name="name">Span and metric name, e.g. <c>"ChatHub.Subscribe"</c>.</param>
    /// <param name="cancellationToken">The hub method's token.</param>
    public static async IAsyncEnumerable<T> Track<T>(
        IAsyncEnumerable<T> source,
        string name,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        using var activity = G9CTelemetry.ActivitySource.StartActivity(name, ActivityKind.Server);
        activity?.SetTag("g9.stream", true);
        var started = Stopwatch.GetTimestamp();
        var items = 0L;
        var outcome = "ok";

        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                T item;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                    item = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    outcome = "cancelled";
                    throw;
                }
                catch (Exception exception)
                {
                    outcome = "faulted";
                    activity?.SetTag("exception.type", exception.GetType().FullName);
                    throw;
                }

                // Recorded on the first item only: what the caller waited for before anything happened.
                if (items == 0) G9CTelemetry.StreamFirstItemMs.Record(Elapsed(started), new KeyValuePair<string, object?>("g9.method", name));
                items++;
                yield return item;
            }
        }
        finally
        {
            // A consumer that stops early never reaches the end of the loop, so the numbers are recorded
            // here: an abandoned stream is exactly the case worth seeing.
            await enumerator.DisposeAsync().ConfigureAwait(false);
            var method = new KeyValuePair<string, object?>("g9.method", name);
            G9CTelemetry.StreamItems.Add(items, method);
            G9CTelemetry.StreamDurationMs.Record(Elapsed(started), method);
            activity?.SetTag("g9.stream_items", items);
            activity?.SetTag("g9.outcome", outcome);
        }
    }

    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
