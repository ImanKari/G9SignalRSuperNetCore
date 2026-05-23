using System.Collections.Concurrent;
using System.Threading.Channels;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Process-wide ring buffer + fan-out hub for live server logs. The
///     <see cref="G9CServerLogProvider"/> appends every formatted log entry; subscribers (the
///     <see cref="G9CServerLogHub"/> live page, additional sinks if you add them) receive each
///     entry through a per-subscription bounded channel.
/// </summary>
public sealed class G9CServerLogFeed
{
    private const int BacklogSize = 500;
    private readonly ConcurrentQueue<G9DtServerLogEntry> _backlog = new();
    private readonly ConcurrentDictionary<Guid, ChannelWriter<G9DtServerLogEntry>> _subscribers = new();

    /// <summary>Appends an entry to the ring buffer and fans it out to every live subscriber.</summary>
    public void Append(G9DtServerLogEntry entry)
    {
        _backlog.Enqueue(entry);
        while (_backlog.Count > BacklogSize && _backlog.TryDequeue(out _)) { /* trim */ }

        foreach (var (_, writer) in _subscribers)
            writer.TryWrite(entry);
    }

    /// <summary>
    ///     Returns the current backlog snapshot followed by a long-lived stream of fresh entries
    ///     (drained when <paramref name="ct"/> is cancelled).
    /// </summary>
    public async IAsyncEnumerable<G9DtServerLogEntry> Subscribe(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Bounded channel with DropOldest so a slow subscriber can't blow memory.
        var channel = Channel.CreateBounded<G9DtServerLogEntry>(new BoundedChannelOptions(2048)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel.Writer;

        try
        {
            // Replay backlog first so a freshly-attached client has context.
            foreach (var entry in _backlog) yield return entry;

            // Then stream fresh entries until cancellation.
            while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                while (channel.Reader.TryRead(out var entry))
                    yield return entry;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}

/// <summary>One row in the live log stream.</summary>
/// <param name="UtcTimestamp">When the log entry was produced.</param>
/// <param name="Level">"trace" / "debug" / "info" / "warn" / "error" / "crit".</param>
/// <param name="Category">Source <c>ILogger</c> category (e.g. <c>Microsoft.Hosting.Lifetime</c>).</param>
/// <param name="Message">Formatted message.</param>
/// <param name="Exception">Exception summary (single line) or null.</param>
public sealed record G9DtServerLogEntry(
    DateTime UtcTimestamp,
    string Level,
    string Category,
    string Message,
    string? Exception);
