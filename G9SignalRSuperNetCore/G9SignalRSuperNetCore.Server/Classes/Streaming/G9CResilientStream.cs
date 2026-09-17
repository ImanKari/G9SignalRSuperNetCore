using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace G9SignalRSuperNetCore.Server.Classes.Streaming;

/// <summary>
///     Helpers for hub methods that produce a server-to-client <see cref="IAsyncEnumerable{T}"/>.
///     Wraps a bounded <see cref="Channel{T}"/> in a typed producer/consumer pair so the hub
///     method enjoys backpressure (or a configurable drop policy) without writing the
///     try/finally/Complete dance every time.
/// </summary>
/// <remarks>
///     <para>Zero allocation on the hot path beyond the items themselves: the channel buffer is
///     sized exactly to <see cref="G9DtStreamOptions.Capacity"/> and the consumer reads in place.</para>
///     <para>Prefer <see cref="Run{T}"/>. It owns the producer: linked cancellation when the consumer
///     stops, the producer's exception delivered to the consumer instead of a silent end of stream,
///     and completion in a <c>finally</c>. <see cref="Create{T}"/> is the lower-level pair for callers
///     that need to hand the writer somewhere else, and it leaves all of that to the caller — a
///     producer that keeps writing into a <see cref="G9EStreamDropPolicy.Wait"/> channel after the
///     consumer walked away blocks for ever, and a producer that throws and only disposes its writer
///     ends the stream as if it had succeeded.</para>
///     <para>Capacity counts ITEMS, not bytes. With variable payloads, size the capacity against the
///     largest item you can produce, or keep the items themselves bounded.</para>
/// </remarks>
public static class G9CResilientStream
{
    /// <summary>
    ///     Runs <paramref name="producer"/> against a bounded channel and returns the stream to hand
    ///     straight back to SignalR: <c>return G9CResilientStream.Run&lt;T&gt;((w, t) =&gt; …, ct: ct);</c>
    /// </summary>
    /// <remarks>
    ///     The producer starts when enumeration starts and is owned for its whole life. If the consumer
    ///     stops early — SignalR client disconnects, the caller breaks out of the loop — the producer's
    ///     token is cancelled, so a write blocked on a full channel unblocks, and the producer task is
    ///     awaited before this method returns. If the producer throws, the channel completes WITH that
    ///     exception, so the consumer sees the failure rather than a stream that merely ended.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="producer">Writes items; the token it receives is cancelled when the consumer stops.</param>
    /// <param name="options">Capacity + drop policy. <c>null</c> = the default options.</param>
    /// <param name="cancellationToken">The hub method's token; linked into the producer's token.</param>
    public static async IAsyncEnumerable<T> Run<T>(
        Func<G9CResilientStreamWriter<T>, CancellationToken, Task> producer,
        G9DtStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (producer is null) throw new ArgumentNullException(nameof(producer));

        var (writer, reader) = Create<T>(options);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producing = ProduceAsync(producer, writer, linked.Token);
        try
        {
            await foreach (var item in reader.AsAsyncEnumerable(linked.Token).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            // The consumer is gone: release a producer blocked on a full channel, then wait for it so
            // it cannot outlive the stream and its failure cannot become an unobserved task exception.
            await linked.CancelAsync().ConfigureAwait(false);
            await ObserveAsync(producing).ConfigureAwait(false);
        }
    }

    private static async Task ProduceAsync<T>(
        Func<G9CResilientStreamWriter<T>, CancellationToken, Task> producer,
        G9CResilientStreamWriter<T> writer,
        CancellationToken ct)
    {
        try
        {
            await producer(writer, ct).ConfigureAwait(false);
            writer.Complete();
        }
        catch (Exception exception)
        {
            // The consumer's enumeration throws this instead of ending cleanly on a half-written stream.
            writer.Complete(exception);
        }
    }

    private static async Task ObserveAsync(Task producing)
    {
        try
        {
            await producing.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Already delivered to the consumer through the channel, or caused by cancelling the
            // producer because the consumer stopped. Either way it is not a second failure.
        }
    }

    /// <summary>
    ///     Creates a bounded producer/consumer pair. Hand <c>Reader.AsAsyncEnumerable(...)</c>
    ///     back to SignalR as the hub method's return value, and write items through
    ///     <see cref="G9CResilientStreamWriter{T}.WriteAsync"/>.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="options">Capacity + drop policy. <c>null</c> = the default options.</param>
    public static (G9CResilientStreamWriter<T> Writer, G9CResilientStreamReader<T> Reader) Create<T>(G9DtStreamOptions? options = null)
    {
        options ??= new G9DtStreamOptions();
        var fullMode = options.DropPolicy switch
        {
            G9EStreamDropPolicy.Wait => BoundedChannelFullMode.Wait,
            G9EStreamDropPolicy.DropNewest => BoundedChannelFullMode.DropWrite,
            G9EStreamDropPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
            _ => BoundedChannelFullMode.Wait
        };

        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(options.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = fullMode,
            AllowSynchronousContinuations = false
        });

        return (new G9CResilientStreamWriter<T>(channel.Writer), new G9CResilientStreamReader<T>(channel.Reader));
    }
}

/// <summary>Thin writer wrapper that completes the channel deterministically on dispose.</summary>
public sealed class G9CResilientStreamWriter<T> : IAsyncDisposable
{
    private readonly ChannelWriter<T> _writer;
    private int _completed;

    /// <summary>Initializes the writer.</summary>
    public G9CResilientStreamWriter(ChannelWriter<T> writer) => _writer = writer;

    /// <summary>Writes <paramref name="item"/> respecting the configured backpressure policy.</summary>
    public ValueTask WriteAsync(T item, CancellationToken ct = default) => _writer.WriteAsync(item, ct);

    /// <summary>Tries to write without blocking. Returns false if the buffer is full and the policy is Wait.</summary>
    public bool TryWrite(T item) => _writer.TryWrite(item);

    /// <summary>Completes the stream with an optional terminal exception.</summary>
    public void Complete(Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _writer.TryComplete(error);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Reader wrapper that exposes <see cref="IAsyncEnumerable{T}"/> for SignalR.</summary>
public sealed class G9CResilientStreamReader<T>
{
    private readonly ChannelReader<T> _reader;

    /// <summary>Initializes the reader.</summary>
    public G9CResilientStreamReader(ChannelReader<T> reader) => _reader = reader;

    /// <summary>
    ///     Returns an <see cref="IAsyncEnumerable{T}"/> that yields items as they arrive and
    ///     completes when the writer is completed. Pass this directly to
    ///     <c>return reader.AsAsyncEnumerable(ct);</c> from a hub streaming method.
    /// </summary>
    public async IAsyncEnumerable<T> AsAsyncEnumerable([EnumeratorCancellation] CancellationToken ct = default)
    {
        while (await _reader.WaitToReadAsync(ct).ConfigureAwait(false))
            while (_reader.TryRead(out var item))
                yield return item;
    }
}
