using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Sample.Shared;

/// <summary>
///     Sample plain (no auth) chat hub used by the WebServer + ConsoleClient samples.
///     Demonstrates Bundle 2 attributes (telemetry, rate limiting) and Bundle 2 file upload
///     (resumable, hashed, server-acknowledged progress).
/// </summary>
[G9AttrConnectionLimit(perUser: 5, perIp: 50)]
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

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await Clients.Others.UserJoined(Context.ConnectionId).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Others.UserLeft(Context.ConnectionId).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private IG9UploadService ResolveUploadService()
        => Context.GetHttpContext()?.RequestServices?.GetService(typeof(IG9UploadService)) as IG9UploadService
           ?? throw new InvalidOperationException(
               "IG9UploadService not registered. Call services.AddG9SignalRSuperNetCoreFileUpload(...) at startup.");
}
