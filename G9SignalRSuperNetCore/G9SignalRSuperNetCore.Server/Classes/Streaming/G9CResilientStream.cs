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
///     Zero allocation on the hot path beyond the items themselves: the channel buffer is
///     sized exactly to <see cref="G9DtStreamOptions.Capacity"/> and the consumer reads in
///     place. The Microsoft SignalR docs explicitly call out the channel-completion footgun;
///     this helper removes it by completing the channel in a finally block.
/// </remarks>
public static class G9CResilientStream
{
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
