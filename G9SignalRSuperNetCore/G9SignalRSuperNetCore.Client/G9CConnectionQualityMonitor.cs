using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>How usable a connection currently is, as measured by <see cref="G9CConnectionQualityMonitor"/> (2.9).</summary>
public enum G9EConnectionQualityLevel
{
    /// <summary>Round trip under <see cref="G9CConnectionQualityMonitor.GoodBelowMs"/> (150 ms).</summary>
    Good,

    /// <summary>Round trip under <see cref="G9CConnectionQualityMonitor.FairBelowMs"/> (400 ms).</summary>
    Fair,

    /// <summary>Round trip of 400 ms or more, or the last probe failed (fewer than three failures in a row).</summary>
    Poor,

    /// <summary>
    ///     <see cref="G9CConnectionQualityMonitor.LostAfterFailures"/> (3) probes failed in a row, or the connection is not
    ///     in the <c>Connected</c> state (reconnecting, disconnected).
    /// </summary>
    Lost
}

/// <summary>One connection-quality measurement (2.9).</summary>
/// <param name="RttMs">
///     The measured round-trip time in milliseconds, or <c>-1</c> when there is no measurement (the probe failed, or the
///     connection is not connected).
/// </param>
/// <param name="Level">The quality level derived from the measurement.</param>
/// <param name="MeasuredUtc">When the measurement was taken; <c>default</c> before the first one.</param>
public readonly record struct G9DtConnectionQuality(double RttMs, G9EConnectionQualityLevel Level, DateTime MeasuredUtc);

/// <summary>
///     Measures the round-trip time of a <see cref="HubConnection"/> at a fixed interval by calling the hub method
///     <c>G9Ping(long)</c> (built into every hub deriving from the G9 hub bases since 2.9) and classifies it as
///     <see cref="G9EConnectionQualityLevel"/>, so a UI can show a connection indicator (2.9).
/// </summary>
/// <remarks>
///     <para>Thresholds: <b>Good</b> under 150 ms, <b>Fair</b> under 400 ms, <b>Poor</b> at 400 ms or more or after one
///     failed probe, <b>Lost</b> after three failed probes in a row or whenever the connection is not
///     <c>Connected</c> (a <c>Reconnecting</c> or <c>Closed</c> event is reported as Lost immediately, and a
///     <c>Reconnected</c> event triggers a probe right away).</para>
///     <para>A probe that has not completed within the interval (at least 1 s, at most 10 s) counts as failed. The round
///     trip is measured on this machine with <see cref="Stopwatch.GetTimestamp"/>; the timestamp sent to the server is
///     that clock in milliseconds and is only echoed back.</para>
///     <para><see cref="QualityChanged"/> is raised only when the level changes; <see cref="Current"/> always holds the
///     latest measurement. Before the first probe, <see cref="Current"/> is <c>Good</c> when the connection was
///     connected at construction and <c>Lost</c> otherwise, with <c>RttMs = -1</c>.</para>
///     <para>The server rate-limits <c>G9Ping</c> to 2 calls a second (burst 5) per connection: keep the interval at
///     500 ms or more. Default: 5 seconds.</para>
///     <code>
///         await using var monitor = new G9CConnectionQualityMonitor(client.Connection);
///         monitor.QualityChanged += q =&gt; statusBar.Show(q.Level, q.RttMs);
///         monitor.Start();
///     </code>
/// </remarks>
public sealed class G9CConnectionQualityMonitor : IAsyncDisposable
{
    /// <summary>The hub method probed by default.</summary>
    public const string DefaultPingMethod = "G9Ping";

    /// <summary>Round trips below this many milliseconds are <see cref="G9EConnectionQualityLevel.Good"/>.</summary>
    public const double GoodBelowMs = 150;

    /// <summary>Round trips below this many milliseconds (and not Good) are <see cref="G9EConnectionQualityLevel.Fair"/>.</summary>
    public const double FairBelowMs = 400;

    /// <summary>Consecutive failed probes after which the level is <see cref="G9EConnectionQualityLevel.Lost"/>.</summary>
    public const int LostAfterFailures = 3;

    /// <summary>The probe interval used when none is given: 5 seconds.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan MinProbeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _probeTimeout;
    private readonly string _pingMethod;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private TaskCompletionSource<bool> _wake = NewWake();
    private G9DtConnectionQuality _current;
    private int _consecutiveFailures;
    private Task? _loop;
    private bool _disposed;

    /// <summary>Creates a monitor for <paramref name="connection"/>; call <see cref="Start"/> to begin probing.</summary>
    /// <param name="connection">The connection to measure. The monitor never starts or stops it.</param>
    /// <param name="interval">Time between probes; default 5 seconds. Must be positive.</param>
    /// <param name="pingMethod">The hub method to call; it must take a <c>long</c> and return it. Default <c>G9Ping</c>.</param>
    public G9CConnectionQualityMonitor(HubConnection connection, TimeSpan? interval = null, string pingMethod = DefaultPingMethod)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        var every = interval ?? DefaultInterval;
        if (every <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        if (string.IsNullOrEmpty(pingMethod)) throw new ArgumentException("A ping method name is required.", nameof(pingMethod));

        _interval = every;
        _probeTimeout = every < MinProbeTimeout ? MinProbeTimeout : every > MaxProbeTimeout ? MaxProbeTimeout : every;
        _pingMethod = pingMethod;
        _current = new G9DtConnectionQuality(-1,
            connection.State == HubConnectionState.Connected ? G9EConnectionQualityLevel.Good : G9EConnectionQualityLevel.Lost,
            default);
    }

    /// <summary>The latest measurement.</summary>
    public G9DtConnectionQuality Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    /// <summary>
    ///     Raised when the level changes (not on every probe). Called on a thread-pool thread; marshal to the UI thread
    ///     yourself. An exception thrown by a handler is swallowed so it cannot stop the monitor.
    /// </summary>
    public event Action<G9DtConnectionQuality>? QualityChanged;

    /// <summary>Starts probing. Calling it again while running does nothing.</summary>
    /// <exception cref="ObjectDisposedException">The monitor was disposed.</exception>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(G9CConnectionQualityMonitor));
            if (_loop is not null) return;

            _connection.Reconnecting += OnReconnecting;
            _connection.Reconnected += OnReconnected;
            _connection.Closed += OnClosed;
            var token = _stop.Token;
            _loop = Task.Run(() => RunAsync(token));
        }
    }

    /// <summary>Stops probing and detaches from the connection's events. The connection itself is left alone.</summary>
    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            loop = _loop;
        }

        _stop.Cancel();
        if (loop is not null)
        {
            _connection.Reconnecting -= OnReconnecting;
            _connection.Reconnected -= OnReconnected;
            _connection.Closed -= OnClosed;
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // The loop only ends by cancellation; nothing to report on the way out.
            }
        }

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await ProbeAsync(ct).ConfigureAwait(false);

            TaskCompletionSource<bool> wake;
            lock (_gate) wake = _wake;
            await Task.WhenAny(Task.Delay(_interval, ct), wake.Task).ConfigureAwait(false);
            lock (_gate)
            {
                if (_wake.Task.IsCompleted) _wake = NewWake();
            }
        }
    }

    private async Task ProbeAsync(CancellationToken ct)
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Report(G9EConnectionQualityLevel.Lost, -1);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_probeTimeout);
        try
        {
            await _connection.InvokeAsync<long>(_pingMethod, ToMilliseconds(started), timeout.Token).ConfigureAwait(false);
            var rttMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Report(Classify(rttMs), rttMs);
        }
        catch (Exception)
        {
            if (ct.IsCancellationRequested) return; // stopping, not failing

            var failures = Interlocked.Increment(ref _consecutiveFailures);
            var lost = failures >= LostAfterFailures || _connection.State != HubConnectionState.Connected;
            Report(lost ? G9EConnectionQualityLevel.Lost : G9EConnectionQualityLevel.Poor, -1);
        }
    }

    private static G9EConnectionQualityLevel Classify(double rttMs) =>
        rttMs < GoodBelowMs ? G9EConnectionQualityLevel.Good
        : rttMs < FairBelowMs ? G9EConnectionQualityLevel.Fair
        : G9EConnectionQualityLevel.Poor;

    /// <summary>A <see cref="Stopwatch"/> timestamp in milliseconds (computed in floating point, so it cannot overflow).</summary>
    private static long ToMilliseconds(long stopwatchTimestamp) => (long)(stopwatchTimestamp * (1000.0 / Stopwatch.Frequency));

    private void Report(G9EConnectionQualityLevel level, double rttMs)
    {
        var quality = new G9DtConnectionQuality(rttMs, level, DateTime.UtcNow);
        bool changed;
        lock (_gate)
        {
            if (_disposed) return;
            changed = quality.Level != _current.Level;
            _current = quality;
        }

        if (!changed) return;
        try
        {
            QualityChanged?.Invoke(quality);
        }
        catch
        {
            // A subscriber failure must not stop the measurements.
        }
    }

    private Task OnReconnecting(Exception? error)
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Report(G9EConnectionQualityLevel.Lost, -1);
        return Task.CompletedTask;
    }

    private Task OnClosed(Exception? error)
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Report(G9EConnectionQualityLevel.Lost, -1);
        return Task.CompletedTask;
    }

    private Task OnReconnected(string? connectionId)
    {
        // Measure the new connection now instead of waiting for the next tick.
        lock (_gate) _wake.TrySetResult(true);
        return Task.CompletedTask;
    }

    private static TaskCompletionSource<bool> NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
