using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Crypto;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using G9SignalRSuperNetCore.Server.Classes.Groups;
using G9SignalRSuperNetCore.Server.Classes.Presence;
using G9SignalRSuperNetCore.Server.Classes.Streaming;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Sample.Shared;

/// <summary>
///     Sample plain (no auth) chat hub used by the WebServer + ConsoleClient samples.
///     Demonstrates Bundle 2 attributes (telemetry, rate limiting) and Bundle 2 file upload
///     (resumable, hashed, server-acknowledged progress).
/// </summary>
[G9AttrConnectionLimit(perUser: 5, perIp: 50)]
[G9AttrPresenceTracked]
[G9AttrAutoJoinGroup("lobby")]
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    /// <summary>Public route the hub is mapped to.</summary>
    public const string Route = "/chat";

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Sample hub; SignalR Hub<T> requires dynamic code intrinsically.")]
    public ChatHub() { }

    /// <inheritdoc />
    public override string RoutePattern() => Route;

    /// <summary>Broadcasts a chat message to every connected client.</summary>
    /// <param name="user">The display name of the sender.</param>
    /// <param name="message">The message body.</param>
    [G9AttrTelemetry]
    [G9AttrRateLimit(perSecond: 5, burst: 10)]
    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);

    /// <summary>Returns a curated list of recent messages (mock).</summary>
    /// <returns>A snapshot of the most recent messages.</returns>
    [G9AttrTelemetry]
    public Task<List<string>> GetRecentMessages()
    {
        var snapshot = new List<string>(8)
        {
            "[2026-05-23 17:00] Iman: Welcome to G9SignalRSuperNetCore!",
            "[2026-05-23 17:01] Meti: Hi everyone!",
            "[2026-05-23 17:02] Iman: This is a sample message.",
            "[2026-05-23 17:03] Meti: Generated client is awesome!"
        };
        return Task.FromResult(snapshot);
    }

    /// <summary>
    ///     Begins or resumes a resumable file upload. Returns the byte offset the client should
    ///     resume from.
    /// </summary>
    [G9AttrTelemetry]
    public async Task<G9DtBeginUploadResult> BeginUpload(
        string uploadId,
        string fileName,
        long totalBytes,
        int chunkSize,
        string declaredSha256Hex)
    {
        var svc = ResolveUploadService();
        return await svc.BeginAsync(uploadId, fileName, totalBytes, chunkSize, declaredSha256Hex,
            Context.ConnectionAborted).ConfigureAwait(false);
    }

    /// <summary>
    ///     Streams a sequence of byte chunks for a previously-begun upload. Server pushes
    ///     <see cref="IChatClient.UploadProgress"/> at the configured cadence.
    /// </summary>
    [G9AttrTelemetry]
    public async Task<G9DtUploadResult> UploadChunks(string uploadId, IAsyncEnumerable<byte[]> chunks)
    {
        var svc = ResolveUploadService();
        var caller = Clients.Caller;

        return await svc.AppendChunksAsync(
            uploadId,
            chunks,
            async bytes => await caller.UploadProgress(new G9DtUploadProgress
            {
                UploadId = uploadId,
                BytesReceived = bytes,
                TotalBytes = 0 // client already knows its declared total; 0 means "use yours".
            }).ConfigureAwait(false),
            Context.ConnectionAborted).ConfigureAwait(false);
    }

    /// <summary>
    ///     Begins or resumes a server-to-client download of <paramref name="fileName"/>. The
    ///     server returns the file's total length and SHA-256 so the client can verify on
    ///     completion.
    /// </summary>
    [G9AttrTelemetry]
    public Task<G9DtBeginDownloadResult> BeginDownload(string fileName, long resumeFrom, int chunkSize)
    {
        var svc = ResolveUploadService();
        return svc.BeginDownloadAsync(fileName, resumeFrom, chunkSize, Context.ConnectionAborted).AsTask();
    }

    /// <summary>
    ///     Streams the contents of <paramref name="fileName"/> from <paramref name="resumeFrom"/>
    ///     onwards. The hub method becomes a server-to-client streaming method by virtue of
    ///     returning <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    [G9AttrTelemetry]
    public IAsyncEnumerable<byte[]> DownloadChunks(string fileName, long resumeFrom, int chunkSize, CancellationToken cancellationToken)
    {
        var svc = ResolveUploadService();
        return svc.StreamFileAsync(fileName, resumeFrom, chunkSize, cancellationToken);
    }

    // ---------- Bundle 3: groups & presence ----------

    /// <summary>Adds the calling connection to <paramref name="roomName"/> and notifies the room.</summary>
    [G9AttrTelemetry]
    [G9AttrRateLimit(perSecond: 5, burst: 10)]
    public async Task JoinRoom(string roomName)
    {
        ArgumentException.ThrowIfNullOrEmpty(roomName);
        var groups = ResolveGroupManager();
        await groups.JoinAsync(Context.ConnectionId, roomName, Context.ConnectionAborted).ConfigureAwait(false);
        await Clients.Group(roomName).UserJoined(Context.ConnectionId).ConfigureAwait(false);
    }

    /// <summary>Removes the calling connection from <paramref name="roomName"/>.</summary>
    [G9AttrTelemetry]
    [G9AttrRateLimit(perSecond: 5, burst: 10)]
    public async Task LeaveRoom(string roomName)
    {
        ArgumentException.ThrowIfNullOrEmpty(roomName);
        var groups = ResolveGroupManager();
        await Clients.Group(roomName).UserLeft(Context.ConnectionId).ConfigureAwait(false);
        await groups.LeaveAsync(Context.ConnectionId, roomName, Context.ConnectionAborted).ConfigureAwait(false);
    }

    /// <summary>Broadcasts <paramref name="message"/> only to members of <paramref name="roomName"/>.</summary>
    [G9AttrTelemetry]
    [G9AttrRateLimit(perSecond: 5, burst: 10)]
    public Task SendToRoom(string roomName, string user, string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(roomName);
        return Clients.Group(roomName).ReceiveMessage($"#{roomName} {user}", message);
    }

    /// <summary>Returns a snapshot of the connection ids in <paramref name="roomName"/>.</summary>
    [G9AttrTelemetry]
    public Task<List<string>> ListRoomMembers(string roomName)
    {
        ArgumentException.ThrowIfNullOrEmpty(roomName);
        var groups = ResolveGroupManager();
        return Task.FromResult(groups.GetMembers(roomName).ToList());
    }

    /// <summary>Returns the list of currently-online user ids (or connection ids when unauthenticated).</summary>
    [G9AttrTelemetry]
    public Task<List<string>> ListOnlineUsers()
    {
        var presence = ResolvePresenceTracker();
        return Task.FromResult(presence.OnlineUsers().ToList());
    }

    // ---------- Bundle 4: streaming & resilience ----------

    /// <summary>
    ///     Server-to-client stream that emits a tick every <paramref name="intervalMs"/> ms
    ///     for <paramref name="count"/> ticks. Demonstrates the <c>G9CResilientStream</c>
    ///     helper with backpressure handling.
    /// </summary>
    /// <param name="count">Number of ticks to produce. Range 1..1000.</param>
    /// <param name="intervalMs">Delay between ticks in milliseconds. Range 10..10000.</param>
    /// <param name="cancellationToken">Cancellation; triggered automatically when the client unsubscribes.</param>
    [G9AttrTelemetry]
    [G9AttrStreamBackpressure(capacity: 64, dropPolicy: G9EStreamDropPolicy.Wait)]
    public IAsyncEnumerable<int> LiveTickerStream(int count, int intervalMs, CancellationToken cancellationToken)
    {
        if (count <= 0 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "Range 1..1000.");
        if (intervalMs < 10 || intervalMs > 10000) throw new ArgumentOutOfRangeException(nameof(intervalMs), "Range 10..10000 ms.");

        // The attribute drives the buffer config so future tooling can scaffold it for free.
        var attr = (G9AttrStreamBackpressureAttribute?)System.Attribute.GetCustomAttribute(
            typeof(ChatHub).GetMethod(nameof(LiveTickerStream))!,
            typeof(G9AttrStreamBackpressureAttribute));
        var (writer, reader) = G9CResilientStream.Create<int>(attr?.ToOptions());

        // Producer task — runs in the background until count is reached or the client unsubscribes.
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < count && !cancellationToken.IsCancellationRequested; i++)
                {
                    await writer.WriteAsync(i, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* normal: client unsubscribed */ }
            catch (Exception ex) { writer.Complete(ex); return; }
            writer.Complete();
        }, cancellationToken);

        return reader.AsAsyncEnumerable(cancellationToken);
    }

    /// <summary>
    ///     Client-to-server bulk-counter stream. Returns the total number of items the client
    ///     pushed before completing the stream — exercises backpressure on the server side.
    /// </summary>
    [G9AttrTelemetry]
    public async Task<long> BulkCounterStream(IAsyncEnumerable<int> stream)
    {
        long total = 0;
        await foreach (var item in stream.WithCancellation(Context.ConnectionAborted).ConfigureAwait(false))
        {
            total += item;
        }
        return total;
    }

    // ---------- Bundle 5: app-level encryption (no TLS required) ----------

    /// <summary>
    ///     Returns the server's long-term P-256 public key (SEC1 uncompressed, 65 bytes). The
    ///     client pins this once after the first connect. Trust-on-first-use unless the
    ///     consumer hard-codes the expected value.
    /// </summary>
    [G9AttrTelemetry]
    public Task<byte[]> GetServerPublicKey()
    {
        var hs = ResolveHandshake();
        return Task.FromResult(hs.StaticPublicKey.ToArray());
    }

    /// <summary>
    ///     Decrypts <paramref name="envelope"/> using a session key derived from the
    ///     handshake the client has already done with this connection's
    ///     <paramref name="ephemeralPublicKey"/>, then echoes the plaintext back sealed under
    ///     the same key. Demonstrates round-trip ChaCha20-Poly1305 over an unauthenticated
    ///     SignalR connection.
    /// </summary>
    [G9AttrEncrypted]
    [G9AttrTelemetry]
    public Task<byte[]> EncryptedEcho(byte[] ephemeralPublicKey, byte[] envelope)
    {
        var hs = ResolveHandshake();
        var key = hs.DeriveSessionKey(ephemeralPublicKey);
        var plaintext = G9CHandshake.Open(key, envelope);
        return Task.FromResult(G9CHandshake.Seal(key, plaintext));
    }

    /// <summary>
    ///     Caches the session key for this connection so subsequent calls can seal/open without
    ///     re-shipping the ephemeral public key. Returns <c>true</c> when the key is accepted.
    /// </summary>
    [G9AttrTelemetry]
    public Task<bool> BeginSession(byte[] ephemeralPublicKey)
    {
        var hs = ResolveHandshake();
        var sealer = ResolveSealer();
        var key = hs.DeriveSessionKey(ephemeralPublicKey);
        sealer.SetSession(Context.ConnectionId, key);
        return Task.FromResult(true);
    }

    /// <summary>
    ///     Session-keyed echo: relies on the session key cached by <see cref="BeginSession"/>.
    ///     Demonstrates the steady-state encryption pattern for a long-lived connection.
    /// </summary>
    [G9AttrEncrypted]
    [G9AttrTelemetry]
    public Task<byte[]> EncryptedEchoSession(byte[] envelope)
    {
        var sealer = ResolveSealer();
        var plaintext = sealer.Open(Context.ConnectionId, envelope);
        return Task.FromResult(sealer.Seal(Context.ConnectionId, plaintext));
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await Clients.Others.UserJoined(Context.ConnectionId).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Auto-leave every group the connection was a member of so the in-process index doesn't leak.
        var groups = Context.GetHttpContext()?.RequestServices?
            .GetService(typeof(G9CGroupManager<ChatHub>)) as G9CGroupManager<ChatHub>;
        if (groups is not null)
            await groups.LeaveAllAsync(Context.ConnectionId).ConfigureAwait(false);

        await Clients.Others.UserLeft(Context.ConnectionId).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private IG9UploadService ResolveUploadService()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(IG9UploadService)) as IG9UploadService
           ?? throw new InvalidOperationException(
               "IG9UploadService not registered. Call services.AddG9SignalRSuperNetCoreFileUpload(...) at startup.");

    private G9CGroupManager<ChatHub> ResolveGroupManager()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(G9CGroupManager<ChatHub>)) as G9CGroupManager<ChatHub>
           ?? throw new InvalidOperationException(
               "G9CGroupManager<ChatHub> not registered. Call services.AddG9SignalRSuperNetCoreGroups<ChatHub>() at startup.");

    private G9CPresenceTracker ResolvePresenceTracker()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(G9CPresenceTracker)) as G9CPresenceTracker
           ?? throw new InvalidOperationException(
               "G9CPresenceTracker not registered. Call services.AddG9SignalRSuperNetCorePresence() at startup.");

    private G9CHandshake ResolveHandshake()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(G9CHandshake)) as G9CHandshake
           ?? throw new InvalidOperationException(
               "G9CHandshake not registered. Call services.AddG9SignalRSuperNetCoreHandshake() at startup.");

    private G9CSessionSealer ResolveSealer()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(G9CSessionSealer)) as G9CSessionSealer
           ?? throw new InvalidOperationException(
               "G9CSessionSealer not registered. Call services.AddG9SignalRSuperNetCoreHandshake() at startup.");
}
