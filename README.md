# G9SignalRSuperNetCore

[![NuGet — Server](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.svg?style=flat-square&label=Server)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server/)
[![NuGet — Client](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Client.svg?style=flat-square&label=Client)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![AOT-safe](https://img.shields.io/badge/NativeAOT-ready-success?style=flat-square)](#maui-and-nativeaot-support)
[![MAUI](https://img.shields.io/badge/MAUI-Android%20%7C%20iOS%20%7C%20Mac%20%7C%20Win-7160E8?style=flat-square)](#maui-and-nativeaot-support)
[![Generator](https://img.shields.io/badge/SourceGen-Incremental-3E8FFF?style=flat-square)](#auto-generated-typed-client)

**G9SignalRSuperNetCore** is a strongly-typed, AOT-friendly, scale-aware wrapper around ASP.NET Core SignalR that lets you build real-time hubs and clients with less ceremony and more safety.

It bundles the things most SignalR projects end up reinventing — typed proxies, JWT authentication, per-user session state, declarative policy attributes (rate limit, connection limit, role/claim guards, telemetry), resumable file upload with progress and SHA-256 verification — and ships them as opt-in, AOT-safe, allocation-conscious primitives.

> Drop reflection-based runtime proxies, get a build-time-generated typed client. Drop static per-process state, get a pluggable session store. Add `[G9AttrRateLimit]` to a method and you're rate-limited; add `[G9AttrTelemetry]` and you have OpenTelemetry traces. Keep the SignalR programming model you already know.

**Current release: 2.10.0** — the four NuGet packages below, and two npm packages: the TypeScript twin `@g9tm/signalr-supernetcore-client` 2.10.0 for browsers, Node **and Lynx**, and its Lynx platform layer `@g9tm/signalr-supernetcore-lynx` 2.10.0 ([TypeScript and Lynx clients](#typescript-client-parity-rule)). See [What's new in 2.10](#210--lynx-the-typescript-client-on-lynx-a-native-websocket-and-files-npm-under-g9tm) and [2.9 → 2.10](#29--210).

---

## Table of contents

- [What's new](#whats-new)
  - [2.10 — Lynx: the TypeScript client on Lynx, a native WebSocket and files, npm under @g9tm](#210--lynx-the-typescript-client-on-lynx-a-native-websocket-and-files-npm-under-g9tm)
  - [2.9 — Scoped rate limits, auth-route throttle, upload ownership, permissions, connection index and quality](#29--scoped-rate-limits-auth-route-throttle-upload-ownership-permissions-connection-index-and-quality)
  - [2.8 — Stateful reconnect and WebSockets-first (opt-in), thread-safe reconnect jitter, tagged releases](#28--stateful-reconnect-and-websockets-first-opt-in-thread-safe-reconnect-jitter-tagged-releases)
  - [2.7 — Awaited calls really wait, and four correctness fixes from an external review](#27--awaited-calls-really-wait-and-four-correctness-fixes-from-an-external-review)
  - [2.6 — MessagePack hub protocol (opt-in), generated-client and registration fixes](#26--messagepack-hub-protocol-opt-in-generated-client-and-registration-fixes)
  - [2.5.3 — Client multi-targets netstandard2.1 (Unity)](#253--client-multi-targets-netstandard21-unity)
  - [2.5.1 — Hub-filter single-constructor fix](#251--hub-filter-single-constructor-fix)
  - [2.5 — Server policy-rejection logging](#25--server-policy-rejection-logging)
  - [2.2 / 2.3 / 2.4 — Groups & presence, streaming & resilience, distributed & secure](#22--23--24--groups--presence-streaming--resilience-distributed--secure)
  - [2.1 — Policy attributes + resumable file upload](#21--policy-attributes--resumable-file-upload)
  - [2.0 — Foundation hardening (Sept 2026)](#20--foundation-hardening-sept-2026)
- [Why this library](#why-this-library)
- [Packages](#packages)
- [MAUI and NativeAOT support](#maui-and-nativeaot-support)
- [Architecture overview](#architecture-overview)
- [Getting started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
- [Quick sample (no auth)](#quick-sample-no-auth)
- [Sample with JWT authentication](#sample-with-jwt-authentication)
- [Sample with sessions](#sample-with-sessions)
- [Sample with JWT + sessions (recommended)](#sample-with-jwt--sessions-recommended)
- [Pluggable session store](#pluggable-session-store)
- [Auto-generated typed client](#auto-generated-typed-client)
- [Client features](#client-features)
- [TypeScript client (parity rule)](#typescript-client-parity-rule)
- [Policy attributes](#policy-attributes)
- [Resumable file upload](#resumable-file-upload)
- [Resumable file download](#resumable-file-download)
- [Groups and presence](#groups-and-presence)
- [Server streams with backpressure](#server-streams-with-backpressure)
- [Resilient client reconnect](#resilient-client-reconnect)
- [MessagePack hub protocol (opt-in)](#messagepack-hub-protocol-opt-in)
- [App-level encryption (no TLS required)](#app-level-encryption-no-tls-required)
- [Distributed backplane (interface)](#distributed-backplane-interface)
- [Telemetry, metrics, and stable error codes](#telemetry-metrics-and-stable-error-codes)
- [JWT helper](#jwt-helper)
- [Scaling to many connections](#scaling-to-many-connections)
- [Thread safety contract](#thread-safety-contract)
- [Console test harness (Consolonia)](#console-test-harness-consolonia)
- [Build and test](#build-and-test)
- [Project layout](#project-layout)
- [Migration guide](#migration-guide)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## What's new

### 2.10 — Lynx: the TypeScript client on Lynx, a native WebSocket and files, npm under @g9tm

The .NET packages are unchanged; this release is about the TypeScript side, which now also runs on
[Lynx](https://lynxjs.org) (ReactLynx apps on Android, iOS, Windows and macOS), and about one version for everything.

**Added — the client runs on Lynx's background thread.** Lynx's JavaScript runtime has no `Blob`, `TextEncoder`,
`btoa`/`atob`, `URL` or `crypto`, no `BigInt` you can rely on, and its release builds have **no WebSocket** (the
devtool's `LynxWebSocketModule` is debug-only and text-only). The client no longer needs any of them: base64 and UTF-8
are built in, uploads read any `G9ByteSource`, `downloadBytes()` needs no Blob, and `G9ClientOptions` takes a
`webSocket` constructor, `skipNegotiation`, `eventSource`, `httpClient` and `httpTimeoutMs`. Every suite also runs in a
Lynx-shaped sandbox (no `BigInt`, `Intl`, `TextEncoder`, `URL`, `Blob`, `WebSocket`; timers only as bundle-scope
identifiers) so a dependency on a missing API fails in CI, not on a device.

**Added — `@g9tm/signalr-supernetcore-lynx`, the Lynx platform layer.** A native module (`G9SignalRLynxModule`,
linked by Lynx Autolink) gives Lynx a **binary** WebSocket — OkHttp on Android, `NSURLSessionWebSocketTask` on iOS, Node's
WebSocket through a Lynxtron preload on Windows/macOS — so the MessagePack protocol works, plus the file access the
transfers need. `createLynxClient(options)` picks everything: the native WebSocket straight to the hub (no negotiate
request), the platform's WebSocket on Lynx for Web, or long polling over Lynx's `fetch` when there is no WebSocket at all
(with a `URL` shim for negotiation).

```ts
import { createLynxClient, lynxFileSystem } from '@g9tm/signalr-supernetcore-lynx';
import { G9FileUploader } from '@g9tm/signalr-supernetcore-client';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

const client = createLynxClient({ url: 'https://host/chat', protocol: new MessagePackHubProtocol(), accessTokenFactory });
await client.start();
const result = await new G9FileUploader(client).uploadFile(photoPath, { fileSystem: lynxFileSystem() });
```

**Added — file transfers by path, exactly like .NET.** `G9FileUploader.uploadFile(path, { fileSystem })` computes the
.NET upload id (`SHA-256(lower(fullPath)|length|lastWriteTicks)`) and `G9FileDownloader.downloadToFile(name, path,
{ fileSystem })` is the twin of `DownloadAsync`: a `.partial` file that survives cancellation and restarts, SHA-256
verification, an atomic rename, a result object. File systems: `nodeFileSystem` (`@g9tm/signalr-supernetcore-client/node`)
and `lynxFileSystem()`. Two former "deliberate differences" of the TypeScript twin are gone.

**Changed — npm scope `@g9tm`, one version.** The TypeScript twin is now `@g9tm/signalr-supernetcore-client` (it was
`@g9/signalr-supernetcore-client`, never published). Both npm packages carry the NuGet version (2.10.0);
`js/scripts/check-versions.mjs` fails the build when any of the version places drift. The release pipeline publishes them
to npm when the `NpmToken` secret is set.

**Verified.** 149 TypeScript unit tests (Node: 130 client, 19 Lynx package), the client and the Lynx package end to end
in the Lynx-shaped sandbox, and
**interop with the real ASP.NET Core sample server** from that sandbox over every path: JSON and MessagePack × {native
WebSocket, native WebSocket after negotiation, long polling} — invocations, both stream directions, `G9Ping`, and files
through `G9CUploadService`. The Android core (OkHttp socket, files) runs on the JVM (8 tests) and the module compiles
against the published Lynx 4.1.0 classes with `lynx-processor` generating its Autolink provider; the iOS file core runs
under GNUstep (14 checks). G9SyncData 1.2's TypeScript client runs its whole sync over this transport against the real G9SyncData server, in Node,
in the Lynx-shaped sandbox, and on a real Lynx runtime (PrimJS on an Android emulator, this native module as packed —
binary and text frames). What only a device can prove is listed in
[`js/packages/lynx/Native-Validation-Pending.md`](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/js/packages/lynx/Native-Validation-Pending.md).

### 2.9 — Scoped rate limits, auth-route throttle, upload ownership, permissions, connection index and quality

Fifteen items: six fixes and nine additions. Everything is additive: existing code compiles and binds to the same overloads, and the new features are opt-in. **One new default changes behaviour: the JWT authorize route is now throttled per IP address** (60 calls a minute, burst 20). A few other changes are visible without opting in: abandoned upload partials are now deleted (after 24 h), slow rate limits refill at the declared rate, connection limits are counted per hub, role/claim/telemetry attributes on a hub class now apply, and the .NET uploader waits for a reconnect instead of failing. [2.8 → 2.9](#28--29) lists each of these. **Source change:** none, unless a hub already declares a method named `G9Ping`.

**Fixed — no trim warning in the hub filter (IL2070).** 2.7's stream-aware telemetry decided whether a method streamed by calling `GetInterfaces()` on the runtime type of its result. The trimmer cannot see through that, so the Release build of the server carried an IL2070 warning. The filter now decides once per method, from the *declared* return type: `IAsyncEnumerable<T>` or `ChannelReader<T>`, bare or wrapped in `Task<>` / `ValueTask<>`. It uses only generic-definition checks, which need no annotations. The telemetry tags are unchanged (`g9.stream`, `g9.outcome=stream_started`), `ChannelReader<T>` methods are now recognised too, and the Release build of all nine projects is warning-free again.

**Fixed — class-level role, claim and telemetry attributes apply.** `[G9AttrRequireRole]`, `[G9AttrRequireClaim]` and `[G9AttrTelemetry]` compile on a hub class, but the filter read them from the method alone, so a class-level role check protected nothing. The filter now reads them from the class (and its bases) too: every class-level claim is required, a class-level role set is a gate of its own that must pass in addition to the method's roles (a broader method role never widens the class), and a class-level `[G9AttrTelemetry]` traces every method (its `Name` becomes a prefix; a method's own attribute wins).

**Fixed — connection limits never leak slots.** `[G9AttrConnectionLimit]` counted a connection at connect and, at
disconnect, read the remote IP again from the HTTP context. After an abrupt close that context is already disposed:
the read threw `ObjectDisposedException`, neither counter was decremented, and the hub's own `OnDisconnectedAsync`
was skipped. Every such drop leaked a slot until the user or address got `G9_CONNECTION_LIMIT` for good. The filter now
records exactly which counters a connection took and gives them back exactly once, without touching the HTTP context;
a connection the hub refuses (its `OnConnectedAsync` throws) gives its slots back too, and a failure in the library's
disconnect bookkeeping is logged but never skips the hub's own handler.

**Fixed — each hub counts its own connections.** The per-user and per-IP counters were shared by all hubs of the
process, so connections to one hub used up another hub's limits (a member with a few tabs of the main hub could not
enter a meeting hub declared with `perUser: 10`). Counters are now keyed by hub type.

**Fixed — .NET uploader waits for the connection like the TypeScript twin.** `G9CFileUploader` starts (or resumes) an
upload only once the connection is up again, within `ReconnectGrace`, instead of failing while it reconnects.

**Fixed — slow rate limits refill at the rate you asked for.** The token bucket stored its refill rate as a whole number of fixed-point units per millisecond, with a floor of one. So anything below about 0.5 calls a second (`perSecond: 0.1`, or any per-minute limit) refilled at roughly one call a second. It also read its clock from system boot and compared 32-bit millisecond stamps without wrap-around, so on a machine that had been up for more than 49.7 days the limiter stopped limiting. The rate is now kept as a fraction, and only the time actually converted into tokens is consumed. The clock is process-relative and compared with wrap-around arithmetic.

**Added — rate-limit scopes.** A per-connection limit can be multiplied by opening more connections. `Scope` shares one bucket between every connection of a user, or of an IP address:

```csharp
// 5 invites back to back, then one a second — per USER, however many tabs and devices they have open
[G9AttrRateLimit(perSecond: 1, burst: 5, Scope = G9ERateLimitScope.User)]
public Task SendInvite(string email) => …;

// per remote IP address (configure forwarded headers behind a proxy)
[G9AttrRateLimit(perSecond: 0.2, burst: 3, Scope = G9ERateLimitScope.Ip)]
public Task RequestPasswordReset(string email) => …;
```

- `Connection` (the default) keeps the 2.8 behaviour. `User` keys the bucket by `Context.UserIdentifier` and `Ip` by the remote address. A connection without one falls back to its own per-connection bucket.
- Buckets are still per method name, and a user or IP bucket is not per hub: two hubs with a user-scoped method of the same name share one bucket, with the limits of whichever call created it. Give such methods distinct names if they need separate allowances.
- A shared bucket lives exactly as long as the connections that share it. The filter counts live connections per user and per address and frees the bucket when the **last** of them disconnects; the idle sweep remains the fallback. The tests check both halves: a user's second connection is refused once the first has spent the allowance, one of two connections leaving does not reset it, and a new connection after both have left starts full.

**Added — the JWT authorize route is throttled per IP (on by default).** Clients could call `G9GetJwtHub.Authorize`, the route that checks credentials and issues tokens, as fast as they liked, which made it a password-guessing endpoint. Every call is now charged to the caller's IP address first. A caller over the allowance gets `IsAccepted = false` with `RejectionReason = "G9_RATE_LIMITED"`, your `authenticate` delegate is not called, and a warning is logged (event id 9102). The defaults are 60 calls a minute with a burst of 20 per address. Change or disable them with the new overload:

```csharp
builder.Services.AddSignalRSuperNetCoreJwt(ChatHub.HubRoute, ChatHub.TokenValidationParameters,
    configureAuth: auth =>
    {
        auth.AuthorizePerMinutePerIp = 30;
        auth.AuthorizeBurstPerIp     = 10;
        // auth.ThrottleEnabled      = false;   // turn it off
    });
```

On the client, `AuthorizeAsync` returns that result like any other rejection:

```csharp
var result = await client.AuthorizeAsync(credentials);
if (!result.IsAccepted && result.RejectionReason == "G9_RATE_LIMITED")
    ShowToast("Too many sign-in attempts. Try again in a minute.");
```

Calls with no known remote address (a non-TCP transport) are not throttled. Behind a reverse proxy, enable the forwarded-headers middleware; otherwise every client is throttled as the proxy.

**Added — upload ownership: per-user namespaces, randomized committed names, an authorization hook, and a cleanup that actually runs.** Until now the upload service knew nothing about *who* was uploading. In 2.9 the hub can tell it, and the host decides what that owner may do:

```csharp
builder.Services.AddG9SignalRSuperNetCoreFileUpload(opt =>
{
    opt.RootDirectory           = Path.Combine(builder.Environment.ContentRootPath, "uploads");
    opt.PerUserNamespace        = true;                     // partials are kept apart per owner
    opt.RandomizeCommittedNames = true;                     // commit as {guid}{.ext}, never the client's name
    opt.CleanupInterval         = TimeSpan.FromMinutes(10); // the default; null turns the sweep off
    opt.Authorize = async (ctx, ct) => ctx.Operation switch
    {
        G9EUploadOperation.Begin  => ctx.OwnerId is not null && await quotas.HasRoomAsync(ctx.OwnerId, ctx.TotalBytes, ct),
        G9EUploadOperation.Append => ctx.OwnerId is not null,
        G9EUploadOperation.BeginDownload or G9EUploadOperation.Download
                                  => ctx.OwnerId is not null && await files.CanReadAsync(ctx.OwnerId, ctx.FileName, ct),
        _ => false
    };
});
```

```csharp
// In the hub (Uploads is the IG9UploadService, injected): pass the owner. The old overloads still exist and mean ownerId: null.
public Task<G9DtBeginUploadResult> BeginUpload(string uploadId, string fileName, long totalBytes, int chunkSize, string sha256) =>
    Uploads.BeginAsync(Context.UserIdentifier, uploadId, fileName, totalBytes, chunkSize, sha256, Context.ConnectionAborted).AsTask();

public Task<G9DtUploadResult> UploadChunks(string uploadId, IAsyncEnumerable<byte[]> chunks) =>
    Uploads.AppendChunksAsync(Context.UserIdentifier, uploadId, chunks, onProgress: null, Context.ConnectionAborted).AsTask();
```

- **`PerUserNamespace`.** With an owner, a partial and its metadata are stored as `{owner}-{hash}__{uploadId}`. `{owner}` is the owner id with every character outside `[A-Za-z0-9-]` replaced by `-`, cut to 32 characters, and `{hash}` is 16 hex characters of the SHA-256 of the exact owner id. Two users can never resume, append to or collide with each other's uploads, even when their clients pick the same upload id. The hash is what separates owners such as `a.b` and `a-b`, which reduce to the same readable part. Uploads without an owner share one anonymous namespace.
- **`RandomizeCommittedNames`.** The committed file is named `{32 hex}{extension}`, where the extension is the declared one lower-cased, reduced to `[a-z0-9.]` and cut to 16 characters. The client's file name never touches the disk; it stays in the upload metadata. Because a name no longer identifies content, `BeginAsync` skips its "already committed under this name" shortcut.
- **`StoredFileName`.** A new property on `G9DtUploadResult` and `G9DtBeginUploadResult`, in the server DTOs and in their client twins. The member name is the same on both sides, so the wire stays compatible and a 2.8 client simply ignores it. It holds the committed name relative to `RootDirectory`, which is what you store and later pass to `BeginDownloadAsync`. It is set on every completed upload, randomized or not.
- **`Authorize`.** Called before every operation with a `G9DtUploadAuthorizationContext(Operation, OwnerId, UploadId, FileName, TotalBytes)`. On append, `TotalBytes` comes from the upload's metadata. On downloads it is the file size and `UploadId` is empty. A refusal is `G9_UPLOAD_FORBIDDEN`: `BeginAsync`, `BeginDownloadAsync` and `StreamFileAsync` throw `InvalidOperationException` with that message, and `AppendChunksAsync` returns a `Failed` result with that `ErrorCode`. SignalR passes only a `HubException`'s message on to the client. To let clients see this code (and the other `G9_UPLOAD_*` codes that `BeginAsync` throws), rethrow it in the hub method: `catch (InvalidOperationException e) when (e.Message.StartsWith("G9_", StringComparison.Ordinal)) { throw new HubException(e.Message); }`.
- **Owner-aware overloads on `IG9UploadService`.** `BeginAsync(ownerId, …)` and `AppendChunksAsync(ownerId, …)`, plus `BeginDownloadAsync(ownerId, …)` and `StreamFileAsync(ownerId, …)` so the hook sees who is downloading. The new overloads have default implementations that call the old ones and ignore the owner, so a custom `IG9UploadService` keeps compiling.
- **The cleanup runs.** `CleanupExpiredPartials()` existed, but nothing called it, so abandoned partials stayed on disk for ever. `AddG9SignalRSuperNetCoreFileUpload` now registers the hosted `G9CUploadCleanupService`, which calls it every `CleanupInterval`. The sweep also treats a partial's `.bin` and `.meta` as one unit now. It judges them by the latest write to either file and deletes them together (the `.meta` only once the `.bin` is gone), and it skips an upload that is writing at that moment. Before, each file was judged alone, so a slow upload could lose its metadata while its `.bin` was still growing.

On a multi-user server, turn on both `PerUserNamespace` and `RandomizeCommittedNames`. The namespace separates uploads in flight; random names stop committed files from being found, or refused, by name.

**Added — `[G9AttrRequirePermission]`: application permissions, decided by your code.** Roles and claims come from the token, but many permissions live in a database. The new attribute names a permission and hands the decision to an `IG9HubPermissionHandler` that you register:

```csharp
[G9AttrRequirePermission("chat.use")]                  // class level: every method needs it
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    [G9AttrRequirePermission("rooms.moderate")]        // method level: needed IN ADDITION
    public Task Kick(string room, string user) => …;
}

public sealed class DbPermissionHandler(AppDb db) : IG9HubPermissionHandler
{
    public async ValueTask<bool> IsAllowedAsync(HubInvocationContext context, string permission) =>
        context.Context.UserIdentifier is { } user && await db.HasPermissionAsync(user, permission);
}

builder.Services.AddScoped<IG9HubPermissionHandler, DbPermissionHandler>();
```

- The attribute stacks on methods and classes. Every permission on the method **and** on its hub class must be granted. It is checked after roles and claims and before the rate limit, so a refused call does not use up an allowance.
- A class-level permission covers inherited hub methods too, including the built-in `G9Ping`, so a client needs it for the connection quality monitor to measure anything.
- The handler is resolved per invocation from the invocation's scope, so it can be a scoped service.
- A refusal is `HubException("G9_PERMISSION_REQUIRED")`, counted in `g9.signalr.authorization_rejections` and logged as event 9100. A method that requires a permission while **no** handler is registered is refused too (fail closed), with a warning (event 9103) that names the missing registration.

**Added — `IG9UserConnectionIndex`: who is online, and a way to end a user's sessions.** Opt in with `builder.Services.AddG9SignalRSuperNetCoreConnectionIndex()`. The hub filter keeps it current for every hub.

```csharp
public sealed class AccountService(IG9UserConnectionIndex connections)
{
    public int SignOutEverywhere(string userId) => connections.AbortUser(userId);   // e.g. after a password change

    public bool IsOnline(string userId) => connections.IsOnline(userId);
}
```

It offers `GetConnections(userId)`, `Count(userId)`, `IsOnline(userId)`, `OnlineUsers()`, `AbortUser(userId)` (which returns how many connections it aborted) and `AbortConnection(connectionId)`. It holds each connection's `HubCallerContext`, so aborting works from anywhere, such as an admin endpoint or a background job. An aborted connection leaves the index once SignalR has processed its disconnect, shortly after the call. A connection is added before the hub's own `OnConnectedAsync` and removed before its `OnDisconnectedAsync`. Inside those methods, `Count(user) == 1` therefore means "first connection" and `!IsOnline(user)` means "last one gone". The index counts a user's connections to all hubs together. Connections without a user identifier are not indexed under any user; only `AbortConnection` reaches them. The index is lock-free and per process.

**Added — `G9Ping` and `G9CConnectionQualityMonitor`.** Every hub that derives from the G9 bases now has `G9Ping(long) => long`, an echo for measuring round trips. It is rate-limited to 2 calls a second (burst 5) per connection and left out of generated typed clients. On the client, the monitor calls it at an interval and classifies the result:

```csharp
await using var quality = new G9CConnectionQualityMonitor(client.Connection);   // every 5 s by default
quality.QualityChanged += q => Dispatcher.Post(() =>
    StatusDot.Fill = q.Level switch
    {
        G9EConnectionQualityLevel.Good => Brushes.Green,     // < 150 ms
        G9EConnectionQualityLevel.Fair => Brushes.Orange,    // < 400 ms
        G9EConnectionQualityLevel.Poor => Brushes.Red,       // ≥ 400 ms, or one failed probe
        _                              => Brushes.Gray       // Lost: 3 failed probes in a row, or not connected
    });
quality.Start();
```

`Current` holds the latest `G9DtConnectionQuality(RttMs, Level, MeasuredUtc)`; `RttMs` is `-1` when there is no measurement. `QualityChanged` fires only when the level changes. A `Reconnecting` or `Closed` event is reported as `Lost` at once, and `Reconnected` triggers a probe straight away. The monitor is available on net10.0 and on netstandard2.1 (Unity). Keep the interval at 500 ms or more because of the server's rate limit.

**Added — `HubConnection.WaitUntilConnectedAsync(timeout)`.** It returns at once when the connection is `Connected`. Otherwise it waits for a reconnect (the `Reconnected` event, or a state poll at most every 100 ms) and throws `TimeoutException` when the time is up. Use it before sending after a network change, instead of failing the call while the automatic reconnect is still running:

```csharp
await client.Connection.WaitUntilConnectedAsync(TimeSpan.FromSeconds(10), ct);
await client.Server.SendMessage(user, text);
```

**Added — telemetry sampling.** `[G9AttrTelemetry(SampleRate = 0.05)]` traces 5% of a hot method's calls instead of all of them. The rate runs from 0 to 1 (default 1), and the decision is a uniform random draw per call. Metrics are not sampled.

**Added — the TypeScript twin is part of the contract.** A TypeScript client with the same features, `@g9/signalr-supernetcore-client` 2.9.0, lives in [`js/`](https://github.com/ImanKari/G9SignalRSuperNetCore/tree/main/js) of this repository and is written separately. From 2.9, every change to the public surface of the .NET client must land in the TypeScript package in the same change, and the other way round. The release pipeline type-checks, tests and builds it before any NuGet package is pushed. See [TypeScript client (parity rule)](#typescript-client-parity-rule) for how to install it.

### 2.8 — Stateful reconnect and WebSockets-first (opt-in), thread-safe reconnect jitter, tagged releases

Five items: one fix, one dependency alignment, two features and one release-process change. Both features are opt-in and **off by default**: a client and a server that turn nothing on build, connect and reconnect exactly as they did in 2.7.0 (a test pins that down — one negotiate request, WebSockets, the same two lifecycle events). **Source change:** none.

**Fixed — the reconnect jitter is thread-safe.** `G9CClientReconnectPolicy` drew its ±15% jitter from one `new Random()` captured in a closure. `System.Random` is not thread-safe, and one policy instance may serve several connections, each asking for its next delay from its own reconnect loop. Shared like that, a `Random` hands racing callers the same samples, and its state can be corrupted so that it returns 0 from then on (the well-known failure of the older generator, which netstandard2.1 runtimes still use). A jitter sample that is always 0 means every client retries at exactly 85% of the nominal delay, in step, which is the thundering herd the jitter exists to prevent. The policy now draws from `Random.Shared` on net10.0 and from a `Random` behind a lock on netstandard2.1. The new test asks one policy for 200,000 delays from each of 2 × cores threads: against the 2.7.0 policy a thread saw as few as 640 distinct delays in its last 1,000, now 999 or 1,000, all inside the band.

**Changed — `Microsoft.Extensions.Http.Resilience` 9.10.0 → 10.0.0** (net10.0 build of the client; the netstandard2.1 build never referenced it). It was the one 9.x reference among 10.x packages. It cannot simply go: the public `G9CHttpResilience` is built on it (`ResilienceHandler`, and Polly's `ResiliencePipeline<T>` in its signatures), so removing it, or moving that class to a package of its own, breaks callers and belongs in a major version. 10.0.0 is the *floor* of the 10.x line on purpose. A package dependency is a minimum, its own `Microsoft.Extensions.*` needs (10.0.0) stay below what `Microsoft.AspNetCore.SignalR.Client` 10.0.8 already brings, and an app that references a newer one wins.

**Added — stateful reconnect, opt-in on both sides.** ASP.NET Core 8 taught SignalR to *resume* a connection after a short network break instead of replacing it: both sides buffer what they sent, the client reconnects the transport under the same connection id, and each side replays what the other missed. Nothing is lost and the hub sees no disconnect. The library now exposes it on both ends, on net10.0 **and** netstandard2.1 (`WithStatefulReconnect()` exists in SignalR.Client 8.0.11 too):

```csharp
// Server — allow it on the endpoint; the overload without the flag leaves it off
app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(ChatHub.Route, allowStatefulReconnects: true);
// (AddSignalRSuperNetCoreJwtHub has the same parameter; it applies to the protected hub, not the auth route)

// Optional: the server's replay buffer PER CONNECTION, in bytes (SignalR's default is 100,000)
builder.Services.AddG9SignalRSuperNetCoreStatefulReconnect(bufferSizeBytes: 256 * 1024);

// Client — ask for it (a generated client is partial, so this goes in your part of it)
public partial class ChatHubClient
{
    protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options) =>
        options.UseStatefulReconnect = true;
}
```

It happens only when both sides opted in; otherwise the connection is an ordinary one. The tests cut the TCP connections underneath a live client, over JSON and over MessagePack. With the option, a call issued into the break is answered, the connection id is the same on both sides afterwards, and the client reports no `Reconnecting` / `Reconnected` / `Disconnected` at all. Without it, the same cut gives `Reconnecting` → `Reconnected` and a new connection id. One thing found on the way: SignalR seeds a hub's own `HubOptions<THub>` from the global `HubOptions` member by member and leaves `StatefulReconnectBufferSize` out, so a hub with per-hub options (`AddG9SignalRSuperNetCoreGroups<THub>` creates them) would silently keep the default. `AddG9SignalRSuperNetCoreStatefulReconnect` carries the value over; a hub that was given a size of its own keeps it. See [Stateful reconnect (opt-in)](#stateful-reconnect-opt-in).

**Added — WebSockets first, with a fallback to negotiation.** `options.WebSocketsFirst = true` connects straight over WebSockets without the negotiate request — one round trip less, and no sticky sessions needed behind a load balancer. Skipping negotiation is normally all-or-nothing: where WebSockets are blocked, the connect just fails. Here, a failed first attempt is answered by one more attempt, in the same `ConnectAsync`, with ordinary negotiation over every transport; `StateChanged` reports it as the new `G9EConnectionPhase.TransportFallback` with the first failure as detail.

- **It does not rebuild the connection.** SignalR copies its `HttpConnectionOptions` every time it opens a transport, so the fallback switches `SkipNegotiation` / `Transports` on the options the connection already reads. `client.Connection` stays the same instance: handlers registered on it with `On<T>()`, its timeouts, and a `G9CFileUploader` holding it all survive. A test asserts exactly that.
- **Not every failure falls back.** A 401 / 403 does not — the second attempt would present the same credentials (the test counts the requests: one, and no negotiate). Neither does a refusal by the hub itself (a `HubException` from the handshake: the transport worked), a cancellation by the caller, or a connection that is disposed or already being started.
- If the second attempt fails too, its exception is the one thrown — the same one a client without the option throws.
- Stateful reconnect is agreed during negotiation, so a connection that skips negotiation does not get it (a test documents that). With both options on, stateful reconnect wins and the client negotiates.

See [WebSockets first, with a safe fallback (opt-in)](#websockets-first-with-a-safe-fallback-opt-in) for what it does *not* cover (SignalR's own automatic-reconnect loop).

**Added — releases are tagged.** Neither the repository nor the mirror carried a single tag, so there was no way to get from a package version back to its commit. After the tests and all four package pushes have succeeded, and only on `main`, the pipeline now creates the annotated tag `v<version>` on the released commit and pushes it; the GitHub sync then carries the tags to the mirror. An existing tag is never moved, and a tagging problem is a warning, never a failed release — the packages are already public by then. The build service identity needs one permission for it; see [Releases](#releases).

### 2.7 — Awaited calls really wait, and four correctness fixes from an external review

An independent review of the library (and of G9SyncData, which sits on it) found five issues. All five are fixed here. One of them changes behaviour you may be relying on — read the first item.

**Changed — `await` on a no-result method now waits for the server.** A generated method returning `Task` or `ValueTask` was emitted as [`SendCoreAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.signalr.client.hubconnection.sendcoreasync?view=aspnetcore-10.0), which completes when the message has been written to the connection. Awaiting it therefore returned *before* the server had run the method, and an exception thrown there could not be observed through that task at all — while a method returning `Task<T>` was an acknowledged invocation, so two calls that look the same in your code behaved differently.

From 2.7.0 a no-result method is an acknowledged invocation: `await client.Server.SaveAsync(row)` returns when the server is done, and a server-side failure surfaces as a `HubException` at the caller.

```csharp
// Keep the old fire-and-forget behaviour where you actually want it:
[G9AttrOneWay]
public Task Heartbeat(long ticks) => …;
```

**Source change:** none. **Behaviour change:** calls that used to return immediately now wait for the server. Add `[G9AttrOneWay]` to any hub method where that wait is not wanted (telemetry pings, presence beacons) and regenerate. If you use the MessagePack protocol, no action is needed — the library's shape providers now cover the result type an acknowledged no-result call binds.

**Fixed — rate-limit buckets are released when the connection ends.** `[G9AttrRateLimit]` allocated a token bucket per `(connection, method)` in a singleton dictionary and never removed it. `OnDisconnectedAsync` cleaned up every other per-connection structure but not this one, so on a server with mobile clients — where every tunnel change is a new connection — the dictionary grew for the life of the process. Buckets are now held per connection and dropped in one lookup when it disconnects, with an idle sweep as a fallback for a disconnect that never arrives. `G9CHubFilter.TrackedRateLimitConnections` exposes the live count for a health check.

**Added — `G9CResilientStream.Run` owns the producer.** `Create<T>` hands back a writer/reader pair and leaves the lifetime to you, which has two sharp edges: a producer that keeps writing after the consumer walked away blocks for ever on a `Wait` channel, and a producer that throws but only disposes its writer ends the stream as though it had succeeded — the consumer sees a short stream and no error.

```csharp
public IAsyncEnumerable<Row> Rows(CancellationToken ct) =>
    G9CResilientStream.Run<Row>(async (writer, token) =>
    {
        await foreach (var row in _db.ReadAsync(token))
            await writer.WriteAsync(row, token);   // throw here and the CONSUMER sees it
    }, new G9DtStreamOptions { Capacity = 256 }, ct);
```

`Run` links cancellation to the consumer, so abandoning the stream releases a blocked producer; awaits the producer before returning; and completes the channel *with* the producer's exception. `Create<T>` is unchanged for callers that need the pair. Note that `Capacity` counts items, not bytes.

**Added — stream-aware telemetry.** `[G9AttrTelemetry]` on a streaming method could only time the invocation: the method returns as soon as its iterator exists, long before the first item, so the span described creating the iterator. The filter now says so (`g9.stream`, `g9.outcome=stream_started`) instead of implying it measured the stream, and `G9CStreamTelemetry.Track<T>(stream, name, ct)` measures the enumeration where the item type is known — time to first item, item count, full duration, and how it ended:

```csharp
[G9AttrTelemetry("ChatHub.Subscribe")]
public IAsyncEnumerable<Message> Subscribe(CancellationToken ct) =>
    G9CStreamTelemetry.Track(Produce(ct), "ChatHub.Subscribe", ct);
```

New metrics on the `G9SignalRSuperNetCore` meter: `g9.signalr.stream_items`, `g9.signalr.stream_first_item_ms`, `g9.signalr.stream_duration_ms`.

**Fixed — a completed upload is verified, not assumed from its name.** `BeginAsync` reported `AlreadyCompleted = true` whenever a file already existed under the requested name, without checking it against the declared size or SHA-256. Uploading a *different* file under a name already taken was therefore reported as complete though its bytes were never sent. The committed file must now match the declared length and hash; otherwise the call fails with `G9_UPLOAD_NAME_CONFLICT`. Resuming an upload id with different content (a different name, size or hash) fails with `G9_UPLOAD_METADATA_CONFLICT` rather than silently adopting the new numbers — a resume with a different chunk size is still fine, since that changes only how the bytes are carried.

Neither the upload service nor its hub methods carry an ownership parameter: authorizing who may begin, resume or download an upload remains the host's job, and path sanitisation is not object authorization.

### 2.6 — MessagePack hub protocol (opt-in), generated-client and registration fixes

**Binary hub protocol, NativeAOT-safe.** Two new opt-in packages, `G9SignalRSuperNetCore.Server.MessagePack` and `G9SignalRSuperNetCore.Client.MessagePack`, add SignalR's MessagePack protocol through [Nerdbank.MessagePack](https://aarnott.github.io/Nerdbank.MessagePack/docs/signalr.html). Its serializers come from [PolyType](https://github.com/eiriktsarpalis/PolyType) type shapes generated at compile time, so it stays AOT- and trim-safe. Under Native AOT, Microsoft's SignalR supports [only the JSON protocol](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0#signalr-supports-trimming-and-native-aot).

- The server offers MessagePack **next to** JSON, and each connection picks its protocol in the handshake. Clients can therefore move one at a time.
- The JSON protocol base64-encodes every `byte[]` (+33%). MessagePack sends raw bytes. Measured:
  - a 2 MB file upload put 2.80 MB on the wire over JSON and 2.10 MB over MessagePack;
  - G9SyncData's 500,000-row initial sync downloads 23.1 MB instead of 30.7 MB, with half the client allocations.
- The library adds shapes for its own hub types behind yours (file-transfer results and acknowledgements, presence events, binary chunks). You only declare shapes for your own types. See [MessagePack hub protocol (opt-in)](#messagepack-hub-protocol-opt-in).

**Fixed — generated clients now use the client-library file-transfer types.** A shared hub assembly references the server package, so hub signatures name `G9SignalRSuperNetCore.Server.Classes.FileUpload.*` DTOs. Up to 2.5.3 the generated client kept those server types.

SignalR binds a callback's arguments using the parameter types of the *first* handler registered for it. The generated `UploadProgress(server DTO)` listener therefore broke `G9CFileUploader`'s own acknowledgement handler, which uses the client DTO: its cast failed and no server-acknowledged bytes were ever reported. Over MessagePack no acknowledgement arrived at all.

The generator now emits the identical `G9SignalRSuperNetCore.Client.FileUpload.*` twins (`G9DtBeginUploadResult`, `G9DtUploadResult`, `G9DtBeginDownloadResult`, `G9DtUploadProgress`, `G9EUploadStatus`). The wire format is unchanged. **Source change:** an override of a generated listener that takes one of these types must now use the client type (see [2.5 → 2.6](#25--26)).

**Fixed — route constants in same-project hubs.** For a hub declared in the consuming project, the generator only read a string *literal* from `RoutePattern()`. `=> Route` (the convention the samples use) silently produced `"/" + ClassName`, so the typed client connected to the wrong path and received 404. The generator now resolves `=> Route` / `=> MyHub.Route` constants, and falls back to a `const string Route` before the class name, like it already did for hubs in referenced assemblies.

**Fixed — service registration is idempotent per service collection, not per process.** `AddSignalRSuperNetCoreCore()` and `AddSignalRSuperNetCoreJwt(...)` used process-wide flags. Every service collection after the first (a second host, a test server) silently ran without the hub filter, the user-id provider, the SignalR options or JWT authentication.

**Tests and CI.** A new `G9SignalRSuperNetCore.Tests` project runs a real Kestrel server with the typed client over both protocols. The pipeline now runs it before publishing; its old test step could never run, because a template expression tested a runtime variable.

### 2.5.3 — Client multi-targets netstandard2.1 (Unity)

Makes `G9SignalRSuperNetCore.Client` usable inside game engines and other runtimes that only expose the **.NET Standard 2.1** API surface (notably **Unity 6**, whose scripting runtime cannot load net10 assemblies).

- **`G9SignalRSuperNetCore.Client` now multi-targets `net10.0;netstandard2.1`.** The net10 build is unchanged (full feature set). The netstandard2.1 build is the engine/Unity-friendly surface.
- **Per-TFM dependencies:** net10 keeps `Microsoft.AspNetCore.SignalR.Client` 10.x + `Microsoft.Extensions.Http.Resilience`; netstandard2.1 uses `Microsoft.AspNetCore.SignalR.Client` 8.x (ships a netstandard2.0 asset) and omits `Http.Resilience` (net8+ only).
- **netstandard2.1 build includes:** the core typed client base (`G9SignalRSuperNetCoreClient`), the JWT client base, `G9CClientReconnectPolicy`, `G9DtConnectionState`, `G9DtAuthorizeResult` — enough to connect, invoke, listen, auto-reconnect, and JWT-authenticate.
- **netstandard2.1 build omits (net10 only):** `G9CHttpResilience` (Polly negotiate hardening) and the resumable file upload/download client.
- **Portable rewrites:** net7+ throw-helpers (`ArgumentException.ThrowIfNullOrEmpty`, `ArgumentNullException.ThrowIfNull`) replaced with classic guards; `ValueTask.CompletedTask` → `default`; added an `IsExternalInit` polyfill (under `#if !NET5_0_OR_GREATER`) so `init`/record types compile on netstandard2.1. Behaviour on net10 is unchanged.

**Unity usage:** Unity does not restore NuGet directly and does not run the Roslyn source generator. Import the netstandard2.1 `G9SignalRSuperNetCore.Client.dll` and its transitive dependency closure into `Assets/Plugins` (e.g. via NuGetForUnity), and either hand-write the typed proxy/listeners against your shared hub interfaces or pre-generate them in a netstandard2.1 project and import the DLL. Add a `link.xml` to protect SignalR/G9 types from IL2CPP stripping.

> Note for maintainers: packaged `LICENSE.md` / icon must use `<None Include="..." Pack="true" PackagePath="" />` (the `<None Update=...>` form does not pack under multi-target → NU5030/NU5046).

### 2.5.1 — Hub-filter single-constructor fix

Bug fix for 2.5.0.

- **Fixed:** registering the hub through `AddSignalRSuperNetCoreServerHub<…>()` (or any
  `HubOptions.AddFilter<G9CHubFilter>()`) threw at startup:
  *"Multiple constructors accepting all given argument types have been found in type
  `G9CHubFilter`. There should only be one applicable constructor."* SignalR builds hub filters
  through `ActivatorUtilities.CreateFactory`, which rejects a filter type that exposes more than
  one DI-satisfiable constructor — and 2.5.0 added a second (parameterless) constructor alongside
  the `ILogger`-taking one.
- **Resolution:** `G9CHubFilter` now has exactly **one** constructor with an *optional* logger
  argument — `G9CHubFilter(ILogger<G9CHubFilter>? logger = null)`. DI resolution, the singleton
  registration, `AddFilter<G9CHubFilter>()`, and a manual `new G9CHubFilter()` (unit tests) all
  keep working; the parameterless path still routes logging to `NullLogger`. No behaviour change
  to metrics or the 2.5.0 policy-rejection logging.

### 2.5 — Server policy-rejection logging

Additive, zero-config observability for the hub policy filter.

- `G9CHubFilter` now emits a structured `Warning`-level log entry on **every** policy rejection, so operators can see *why* a hub call or connect was refused instead of only observing the client-side `HubException`. Previously rejections incremented a metric and threw, but produced no server log — a visibility gap for diagnostic / support scenarios.
- The filter resolves an `ILogger<G9CHubFilter>` from DI (wired automatically by `AddSignalRSuperNetCoreCore()`); a parameterless fallback routes to `NullLogger` so `new G9CHubFilter()` and `AddFilter<G9CHubFilter>()` both keep working. The `G9CTelemetry` metrics are unchanged.
- Event ids: `9100` (per-invocation rejection — rate limit / role / claim / connection-required, with `ErrorCode`, `Method`, `ConnectionId`, `UserId`, `Detail`) and `9101` (per-connect connection-limit rejection — `Hub`, `Dimension`, `Key`, `Limit`). Raise the minimum level for the `G9SignalRSuperNetCore.Server.Classes.Filters.G9CHubFilter` category to silence them. See [Policy-rejection logging](#policy-rejection-logging).

### 2.2 / 2.3 / 2.4 — Groups & presence, streaming & resilience, distributed & secure

This release closes out Bundles 3, 4, and 5 in a single combined drop.

**Bundle 3 — Groups & presence**

- `G9CGroupManager<THub>` — strongly-typed in-process group index built on top of SignalR's group machinery. Lock-free membership through `ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>`, snapshots via `ToArray`, automatic prune of empty groups. Resolved through `services.AddG9SignalRSuperNetCoreGroups<THub>()`.
- `G9CPresenceTracker` — per-user connection counts in a single `ConcurrentDictionary<string, int>`. `OnConnected` / `OnDisconnected` are lock-free; transitions between online and offline are published to a `Channel<G9DtPresenceEvent>` so a hosted service or any subscriber can fan them out. Resolved through `services.AddG9SignalRSuperNetCorePresence()`.
- `[G9AttrPresenceTracked]` (class-level) — opt-in flag the central hub filter reads on connect/disconnect to invoke the tracker. Hubs without the attribute pay no extra work.
- `[G9AttrAutoJoinGroup("name")]` (class-level, repeatable) — every new connection is added to the listed groups before `OnConnectedAsync` runs. Implemented as a hub-typed `G9CAutoJoinFilter<THub>`, registered automatically when you call `AddG9SignalRSuperNetCoreGroups<THub>()`. AOT-safe (the generic argument is resolved at compile time).
- The sample `ChatHub` carries `[G9AttrPresenceTracked]` plus `[G9AttrAutoJoinGroup("lobby")]`, and exposes `JoinRoom`, `LeaveRoom`, `SendToRoom`, `ListRoomMembers`, `ListOnlineUsers`. The console harness has a Rooms tab that exercises every method.

**Bundle 4 — Streaming & resilience**

- `G9CResilientStream.Run<T>(producer, options, ct)` — runs a producer against a bounded channel and owns it: cancels it when the consumer stops, awaits it, and completes the channel WITH its exception so a failed producer faults the consumer instead of ending the stream quietly. Prefer it.
- `G9CResilientStream.Create<T>(options)` — the lower-level producer/consumer pair, for callers that hand the writer elsewhere. Completion and producer lifetime are then the caller's.
- `G9DtStreamOptions` — capacity plus drop policy: `Wait`, `DropNewest`, `DropOldest`.
- `[G9AttrStreamBackpressure(capacity, dropPolicy)]` — declarative backpressure metadata. Hub authors can read it back through reflection (`GetCustomAttribute<…>().ToOptions()`) when constructing the stream.
- `G9CClientReconnectPolicy` (client) — drop-in replacement for `WithAutomaticReconnect()` with exponential backoff plus ±15% jitter, configurable max delay, max elapsed time. Wired by default into the generated client base.
- The sample `ChatHub` exposes `LiveTickerStream(count, intervalMs, ct)` (server→client `IAsyncEnumerable<int>`) decorated with `[G9AttrStreamBackpressure(64, Wait)]`, plus `BulkCounterStream(IAsyncEnumerable<int>)` for client→server. The console harness has a Streaming tab with Start/Stop ticker plus a Push 100 → server button (expected total 5050).

**Bundle 5 — Distributed & secure (no-TLS encryption)**

- `G9CHandshake` — managed ECDH P-256 + HKDF-SHA-256 + ChaCha20-Poly1305. Pure `System.Security.Cryptography`: AOT-safe, FIPS-validated on every supported platform, hardware-accelerated on x86-AESNI / Arm-NEON. Why P-256 not X25519/Noise IK: the BCL ships P-256 ECDHE through `ECDiffieHellman` and a vetted `ChaCha20Poly1305`; pulling in third-party crypto would defeat the AOT/MAUI promise, and ECDHE-static + AEAD gives equivalent security guarantees.
- 65-byte SEC1 uncompressed wire format for public keys, 12-byte nonce + ciphertext + 16-byte tag for sealed envelopes.
- `G9CSessionSealer` — per-connection session-key cache. Once a connection has performed `BeginSession`, hub methods can `Seal` / `Open` payloads without round-tripping the ephemeral public key on every call. Keys are zeroed on disconnect by the central hub filter.
- `[G9AttrEncrypted]` — declarative contract on hub methods that work in `byte[]` envelopes.
- `IG9DistributedBackplane` — minimal publish/subscribe abstraction so the in-process group manager and presence tracker can be replaced with a Redis or NATS implementation when scaling out. Default registration is a no-op `G9CInProcessBackplane` for single-process deployments. SignalR's own Redis backplane handles message fan-out; this interface handles the G9-specific membership state.
- The sample `ChatHub` exposes `GetServerPublicKey()`, `EncryptedEcho(byte[] ephemeralPub, byte[] envelope)` (one-shot), `BeginSession(byte[] ephemeralPub)` plus `EncryptedEchoSession(byte[] envelope)` (cached). The console harness has an Encrypted tab that runs the full round-trip and prints the SHA-256 fingerprint of the server's static public key.

**Resumable downloads (symmetric to upload)**

The 2.1 upload pipeline now has a peer for the other direction.

- `G9CFileDownloader` (client) — `DownloadAsync(serverFileName, localTargetPath, progress, ct)`. Resumes from a local `.partial` file, verifies the SHA-256 the server reported in `BeginDownload`, atomically renames on commit. Idempotent fast path: if a fully-downloaded committed file already matches the server's hash, the call returns `Completed` without re-streaming.
- `BeginDownloadAsync` / `StreamFileAsync` on `IG9UploadService` — server-side. `BeginDownloadAsync` rejects path traversal, caches the SHA-256 keyed by `(path, length, lastWriteUtc)` so it isn't recomputed on every begin call, and caps the chunk size at 4 MB. `StreamFileAsync` returns `IAsyncEnumerable<byte[]>` for SignalR's documented server→client streaming pattern.
- New error codes on the client side: `G9_DOWNLOAD_NOT_FOUND`, `G9_DOWNLOAD_HASH_MISMATCH`, `G9_DOWNLOAD_FAILED`.

**Console harness — File transfer tab**

The former File-upload tab is now File transfer with both directions:

- Upload buttons: `[ Upload ]`, `[ Upload then break ]` (cancels at 8 MB to simulate a drop), `[ Resume upload ]`.
- Download buttons: `[ Download ]`, `[ Download then break ]`, `[ Resume download ]`.
- Two progress bars — one for each direction — with throughput and elapsed time.
- A single shared transfer log so retries, breaks, and resumes are visible together.

### 2.1 — Policy attributes + resumable file upload

Version 2.1 adds the production-grade policy surface and a complete resumable file-upload pipeline. Every new feature is opt-in; if you don't apply an attribute or call a helper, runtime cost is zero.

**Declarative policy attributes** (Bundle 2):

- `[G9AttrRateLimit(perSecond: 5, burst: 10)]` — per-(connection, method) lock-free token bucket, released when the connection disconnects. Rejects with `G9_RATE_LIMITED` and emits a metric.
- `[G9AttrConnectionLimit(perUser: 5, perIp: 50)]` — class-level cap on simultaneous connections. Rejects with `G9_CONNECTION_LIMIT` before `OnConnectedAsync` returns.
- `[G9AttrConnectionRequired]` — fail-fast guard for methods that should not run on aborted connections.
- `[G9AttrRequireRole("admin")]` / `[G9AttrRequireRole("a","b")]` — shorthand for role-gated methods, returning `G9_ROLE_REQUIRED`.
- `[G9AttrRequireClaim("scope", "chat:write")]` — claim-gated methods, returning `G9_CLAIM_REQUIRED`.
- `[G9AttrTelemetry]` / `[G9AttrTelemetry("name")]` — emits `ActivitySource` spans (`G9SignalRSuperNetCore`) with connection id, user id, and outcome. On a STREAMING method the span covers the invocation only (the method returns before the first item); wrap the stream with `G9CStreamTelemetry.Track<T>` to measure the enumeration.
- `[G9AttrOneWay]` — makes a generated no-result method fire-and-forget. Without it, awaiting one waits for the server (2.7.0).

**Stable error codes** (`G9CErrorCodes`): `G9_RATE_LIMITED`, `G9_CONNECTION_LIMIT`, `G9_CONNECTION_REQUIRED`, `G9_ROLE_REQUIRED`, `G9_CLAIM_REQUIRED`, `G9_UPLOAD_TOO_LARGE`, `G9_UPLOAD_HASH_MISMATCH`, `G9_UPLOAD_UNKNOWN_ID`, `G9_UPLOAD_FAILED`. Clients switch on `HubException.Message` to render localized errors or trigger recovery.

**Built-in metrics** through `System.Diagnostics.Metrics` on the `G9SignalRSuperNetCore` meter: `g9.signalr.rate_limited_invocations`, `g9.signalr.connection_limit_rejections`, `g9.signalr.authorization_rejections`, `g9.signalr.stream_items`, `g9.signalr.stream_first_item_ms`, `g9.signalr.stream_duration_ms`. Subscribe with OpenTelemetry's `AddMeter("G9SignalRSuperNetCore")`.

**Resumable file upload** (real, not vapourware):

- `IG9UploadService` + `G9CUploadService` on the server. Append-only writes, per-id semaphore for concurrency, atomic commit at SHA-256 verify, configurable `RootDirectory` / `MaxBytes` / `PartialTtl` / `AckEveryNChunks`.
- `G9CFileUploader` on the client. Computes deterministic upload id from `(path, length, lastWriteUtc)`, hashes the file once, calls `BeginUpload` to learn the resume offset, streams the remainder via SignalR client streaming. Exposes a unified `IProgress<G9DtUploadClientProgress>` with locally-counted `BytesSent`, server-acknowledged `BytesAcknowledged`, throughput, and elapsed time. Auto-retries on transient failures with exponential backoff up to `MaxRetries`.
- **Safe resume on disconnect** by design — the partial file is preserved, the next `UploadAsync` call re-runs `BeginUpload`, gets the new offset, seeks the source file, and continues. No duplicate bytes, no corruption.
- **Tamper-detection** — declared SHA-256 is verified before the partial is committed; mismatch deletes the partial and returns `G9_UPLOAD_HASH_MISMATCH`.

**Generator improvements**:

- Detects `IAsyncEnumerable<byte[]>` (and `IAsyncEnumerable<T>` more generally) parameters as client streams and emits the right `InvokeCoreAsync` signature.
- Handles `ValueTask` and `ValueTask<T>` return types throughout.
- Threads `CancellationToken` parameters into the generated proxy without you wiring it.

**Tabbed Consolonia console test harness**:

- Five tabs: Connection, Chat, Recent messages, File upload, Rate-limit test.
- Connect/disconnect, send/receive chat with the typed `Server` proxy, fetch recent messages, upload a file with live progress bar (local + server-acked) and a Cancel button that resumes cleanly, run a 100-message burst that demonstrates the rate limiter rejecting calls with `G9_RATE_LIMITED`.

### 2.0 — Foundation hardening (Sept 2026)

Version 2.0 was the hardening release focused on correctness at scale and platform reach.

- **MAUI and NativeAOT ready.** Every library project declares `IsAotCompatible=true` and `EnableTrimAnalyzer=true`. Castle DynamicProxy is gone; the typed `Server` proxy is produced by a true Roslyn `IIncrementalGenerator` at compile time. Publishes cleanly under `PublishAot=true` with no trim warnings attributable to G9 code.
- **No reflection on the client hot path.** Server-method invocations and listener registrations are concrete typed calls; no `MethodInfo.Invoke`, no boxing of return values.
- **Pluggable session storage.** `IG9SessionStore<TSession>` abstraction. The default `G9CInMemorySessionStore<TSession>` is registered through DI; swap it for a distributed implementation to scale across processes.
- **Lock-free, race-free session counters.** `G9ASession.ConnectionCounts` and `LastActivityDateTime` are mutated through `Interlocked` only.
- **Encapsulated JWT route registry.** Underscore-prefixed static dictionary on `G9GetJwtHub` is replaced by a proper internal registry. `JwtSecurityTokenHandler` is reused process-wide.
- **Cleaner scale-out path.** No correctness-critical state in static fields. The same hub source code runs unchanged behind a Redis backplane or Azure SignalR Service.
- **Warning-clean Release build.** Zero AOT, trim, or compiler warnings attributable to G9 code.

## Why this library

Plain ASP.NET Core SignalR is excellent, but most teams end up writing the same plumbing: a typed proxy for server methods, a registration block for client callbacks, a JWT pipeline that plays nicely with WebSockets, a per-user session, a code-gen step so client and server never drift, and (eventually) rate limits, connection caps, telemetry hooks, and a chunked file-upload protocol. G9SignalRSuperNetCore covers all of that:

- **Strongly-typed server hubs** through a generic `Hub<TClientInterface>` base.
- **Strongly-typed client proxy generated at compile time** — call `client.Server.MyMethod(...)` directly.
- **Listener wiring with no reflection** — generated typed `Connection.On<...>` calls match your interface.
- **JWT authentication out of the box** — separate auth route exchanges credentials for a token, then the protected hub uses `[Authorize]`.
- **Per-user session abstraction** — thread-safe counters, last-activity tracking, cleanup helpers, swappable backend.
- **Declarative policy attributes** — rate limiting (per connection, user or IP), connection caps, role/claim/permission guards, telemetry. Opt-in, lock-free, zero cost when unused.
- **Resumable file upload** — chunk-streamed, SHA-256-verified, append-only, with live progress and safe resume on disconnect.
- **A TypeScript twin, Lynx included** — `@g9tm/signalr-supernetcore-client` gives browser, Node and Lynx front ends the same reconnect, JWT, file-transfer and quality features, kept in parity with the .NET client; `@g9tm/signalr-supernetcore-lynx` adds the native WebSocket and file access Lynx lacks.
- **Scale-out friendly** — no static per-process state on the correctness path.
- **MAUI and NativeAOT friendly** — every hot path free of `Reflection.Emit` and runtime proxy generation.

## Packages

| Package | Purpose |
|---|---|
| `G9SignalRSuperNetCore.Server` | Hub bases, JWT pipeline, session abstraction, attributes, hub filter, file-upload service, embedded source generator |
| `G9SignalRSuperNetCore.Client` | Typed client bases (anonymous + JWT), resumable file uploader, progress DTOs |
| `G9SignalRSuperNetCore.Server.MessagePack` | Opt-in binary hub protocol for servers, offered next to JSON (2.6+) |
| `G9SignalRSuperNetCore.Client.MessagePack` | Opt-in binary hub protocol for clients (2.6+) |

The source generator ships **inside the Server package** under `analyzers/dotnet/cs`. Consumers don't need a separate code-gen package; just reference `G9SignalRSuperNetCore.Server` (server) and `G9SignalRSuperNetCore.Client` (client) and the typed client lights up automatically.

All four packages are versioned together (2.10.0), and their license is MIT. For browser, Node and Lynx clients there are the npm packages **`@g9tm/signalr-supernetcore-client`** 2.10.0 (peer dependency `@microsoft/signalr` ^10) and, for Lynx apps, **`@g9tm/signalr-supernetcore-lynx`** 2.10.0 (native module + Lynx defaults). Both are built from [`js/`](https://github.com/ImanKari/G9SignalRSuperNetCore/tree/main/js) in this repository, with the same version as the NuGet packages; see [TypeScript and Lynx clients](#typescript-client-parity-rule).

All packages target **.NET 10.0** and are AOT-compatible and trim-safe, the MessagePack packages included. **`G9SignalRSuperNetCore.Client` additionally targets `netstandard2.1`** for Unity 6 / engine runtimes (see [2.5.3](#253--client-multi-targets-netstandard21-unity)).

---

## MAUI and NativeAOT support

The library is designed for iOS, Android, MacCatalyst, Windows, and NativeAOT publishes from day one.

- **No `Reflection.Emit`.** The Castle DynamicProxy runtime proxy is gone. The typed `Server` proxy is concrete code emitted by the Roslyn incremental source generator at build time.
- **No reflection on the client hot path.** Listener registrations are typed `Connection.On<...>(...)` calls in generated code. Server-method invocations go straight to `HubConnection.InvokeCoreAsync` / `SendCoreAsync` / `StreamAsyncCore`.
- **Trim and AOT analyzers enabled.** Every library project sets `IsAotCompatible=true`, `EnableTrimAnalyzer=true`, and `IsTrimmable=true`. Public APIs that capture interface generic parameters propagate `[DynamicallyAccessedMembers]`.
- **Source-generated JSON.** The upload service uses `JsonSerializerContext` source generation for its metadata files, so the AOT publish has no reflection-based JSON path.
- **Single intrinsic AOT requirement.** ASP.NET Core SignalR's typed `Hub<T>` base requires dynamic code at runtime to materialize its strongly-typed client proxy on the server side. Hub base constructors are annotated with `[RequiresDynamicCode]` so the build cleanly surfaces this in any AOT-published server. Client-side AOT is unaffected.

To publish a MAUI client in Release with AOT:

```xml
<PropertyGroup Condition="'$(TargetFramework)' == 'net10.0-ios' or '$(TargetFramework)' == 'net10.0-maccatalyst'">
  <PublishAot>true</PublishAot>
  <TrimMode>full</TrimMode>
</PropertyGroup>
```

Then `dotnet publish -f net10.0-ios -c Release` should complete without trim or AOT warnings attributable to G9 code.

> Server-side AOT publish is supported, but ASP.NET Core SignalR itself emits trim/AOT warnings for typed hub proxies; the library's annotations make those warnings visible to you instead of hiding them.

## Architecture overview

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                                  SERVER                                       │
│                                                                               │
│   G9AHubBase<THub, TClient>                                                   │
│   ├─ G9AHubBaseWithJWTAuth<THub, TClient>                                     │
│   ├─ G9AHubBaseWithSession<THub, TClient, TSession>                           │
│   └─ G9AHubBaseWithSessionAndJWTAuth<THub, TClient, TSession>                 │
│                                                                               │
│   ┌───────── Policy & telemetry ─────────┐                                    │
│   │ G9CHubFilter (singleton, lock-free)  │  enforces:                         │
│   │   • G9CTokenBucket per scope+method  │   [G9AttrRateLimit(Scope = …)]     │
│   │   • G9CConnectionCounter per hub,    │   [G9AttrConnectionLimit]          │
│   │     user and IP (released once)      │                                    │
│   │   • role/claim/permission guards     │   [G9AttrRequireRole / Claim]      │
│   │                                      │   [G9AttrRequirePermission]        │
│   │   • IG9UserConnectionIndex (opt-in)  │                                    │
│   │   • ActivitySource spans (sampled)   │   [G9AttrTelemetry]                │
│   │   • G9CTelemetry.Meter (metrics)     │                                    │
│   └──────────────────────────────────────┘                                    │
│                                                                               │
│   IG9SessionStore<TSession> (DI)        IG9UploadService (DI)                 │
│   └─ G9CInMemorySessionStore (default)   └─ G9CUploadService (default)        │
│                                            • partials at /uploads/.partial/  │
│                                            • SHA-256 verified, atomic commit │
│                                                                               │
│   AddSignalRSuperNetCoreCore()                                                │
│   AddG9SignalRSuperNetCoreSessionStore<TSession>()                            │
│   AddG9SignalRSuperNetCoreFileUpload(opt => …)                                │
│   AddG9SignalRSuperNetCoreConnectionIndex()          (2.9, opt-in)            │
│   AddSignalRSuperNetCoreJwt(hubPath, validationParameters)                    │
│   AddSignalRSuperNetCoreJwtHub<THub, TClient>(hubRoute, authRoute, …)         │
│                                                                               │
│   /AuthHub (per-IP throttle)  ─►  /SecureHub  (Authorize)                     │
└──────────────────────────────────────────────────────────────────────────────┘
                                     ▲
                                     │  WebSocket / SSE / LongPolling
                                     ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                                  CLIENT                                       │
│                                                                               │
│   G9SignalRSuperNetCoreClient<TSelf, TServerMethods, TListeners>              │
│   └─ G9SignalRSuperNetCoreClientWithJWTAuth<...>                              │
│                                                                               │
│   client.Server.MyMethod(args)            ── source-generator typed proxy     │
│   listener methods on derived class       ── source-generator typed wiring   │
│   AOT-safe — no Castle, no Reflection.Emit, no MethodInfo.Invoke             │
│                                                                               │
│   G9CFileUploader(connection)                                                 │
│     • SHA-256 hash + deterministic upload id                                  │
│     • BeginUpload → resume offset → stream chunks                             │
│     • IProgress<G9DtUploadClientProgress> (local + server-ack)                │
│     • exponential-backoff retry, safe resume on disconnect                    │
│                                                                               │
│   G9CConnectionQualityMonitor(connection) ── G9Ping RTT → Good/Fair/Poor/Lost │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Getting started

### Prerequisites

- .NET 10.0 SDK or later
- ASP.NET Core 10 project for the server
- Any .NET 10 project for the client (console, MAUI, WPF, Blazor, ASP.NET, etc.), or a .NET Standard 2.1 runtime such as Unity 6 (see [2.5.3](#253--client-multi-targets-netstandard21-unity))
- For the TypeScript client: Node 18+, a modern browser or Lynx (with `@g9tm/signalr-supernetcore-lynx`), with `@microsoft/signalr` ^10

### Install

Server project:

```powershell
dotnet add package G9SignalRSuperNetCore.Server --version 2.10.0
```

Client project:

```powershell
dotnet add package G9SignalRSuperNetCore.Client --version 2.10.0
```

Optional, the binary MessagePack protocol (see [MessagePack hub protocol](#messagepack-hub-protocol-opt-in)):

```powershell
dotnet add package G9SignalRSuperNetCore.Server.MessagePack --version 2.10.0   # server
dotnet add package G9SignalRSuperNetCore.Client.MessagePack --version 2.10.0   # client
```

The source generator is shipped inside the Server package (`analyzers/dotnet/cs`). Both project-reference and NuGet-reference flows pick it up automatically. Keep all G9 packages on the same version.

Browser, Node or Lynx front end: see [TypeScript and Lynx clients](#typescript-client-parity-rule) for installing `@g9tm/signalr-supernetcore-client` 2.10.0 (and `@g9tm/signalr-supernetcore-lynx` on Lynx).

---

## Quick sample (no auth)

A minimal hub, a client interface for server-to-client callbacks, and a console client.

**Shared library — `IChatClient.cs`**

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
    Task UserJoined(string user);
    Task UserLeft(string user);
}
```

**Shared library — `ChatHub.cs`**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;

public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    public const string Route = "/chat";
    public override string RoutePattern() => Route;

    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);

    public override async Task OnConnectedAsync()
    {
        await Clients.Others.UserJoined(Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        await Clients.Others.UserLeft(Context.ConnectionId);
        await base.OnDisconnectedAsync(ex);
    }
}
```

**Server — `Program.cs`**

```csharp
using G9SignalRSuperNetCore.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalRSuperNetCoreCore();   // SignalR + deny policy + UserIdProvider + hub filter

var app = builder.Build();
app.MapGet("/", () => "Chat server");
app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(routePattern: ChatHub.Route);
app.Run();
```

**Client — generated typed client**

The source generator emits `ChatHubClient` from your hub class. In the client project just subclass it and override the listener methods you care about:

```csharp
var client = new MyChatClient("https://localhost:7159");
await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello, world");
Console.ReadLine();
await client.DisconnectAsync();

public sealed class MyChatClient(string url) : ChatHubClient(url)
{
    public override Task ReceiveMessage(string user, string message)
    {
        Console.WriteLine($"[{user}] {message}");
        return Task.CompletedTask;
    }

    public override Task UserJoined(string user) { Console.WriteLine(user + " joined"); return Task.CompletedTask; }
    public override Task UserLeft(string user)   { Console.WriteLine(user + " left");   return Task.CompletedTask; }
}
```

The generator emits the `ChatHubClient` partial class with default no-op overrides; you supply the bodies you need.

---

## Sample with JWT authentication

The library ships a dedicated authentication route (`/AuthHub` by convention) that exchanges arbitrary credentials for a JWT, then a protected hub route (`/SecureHub`) that requires the token. The token is sent on the `access_token` query string so it works over WebSockets out of the box.

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

public class SecureHub : G9AHubBaseWithJWTAuth<SecureHub, IChatClient>
{
    public const string HubRoute  = "/SecureHub";
    public const string AuthRoute = "/AuthHub";
    private const string JwtSecret = "replace-with-a-strong-secret-of-at-least-32-bytes";

    private static readonly G9JWTokenFactory TokenTemplate =
        G9JWTokenFactory.GenerateJWTToken(JwtSecret, "G9TM", "G9TM",
            DateTime.UtcNow.AddDays(3), G9ESecurityAlgorithms.HmacSha256);

    public static TokenValidationParameters TokenValidationParameters => TokenTemplate.ValidationParameters!;

    public override string RoutePattern() => HubRoute;
    public override string AuthAndGetJWTRoutePattern() => AuthRoute;

    public static Task<(G9JWTokenFactory factory, object? extra)> AuthenticateAsync(
        object authorizeData, Hub authHub)
    {
        if (authorizeData?.ToString() == "valid-credentials")
        {
            var token = G9JWTokenFactory.GenerateJWTToken(
                JwtSecret, "Iman", "admin", "G9TM", "G9TM", DateTime.UtcNow.AddDays(3));
            return Task.FromResult<(G9JWTokenFactory, object?)>((token, new { Welcome = "Hi Iman" }));
        }

        return Task.FromResult<(G9JWTokenFactory, object?)>(
            (G9JWTokenFactory.RejectAuthorize("Invalid credentials"), null));
    }

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub authHub) => AuthenticateAsync(authorizeData, authHub);

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub() => TokenValidationParameters;

    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);
}
```

```csharp
// Server registration
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddSignalRSuperNetCoreJwt(
    hubPath: SecureHub.HubRoute,
    validationParameters: SecureHub.TokenValidationParameters);

var app = builder.Build();

app.AddSignalRSuperNetCoreJwtHub<SecureHub, IChatClient>(
    hubRoutePattern:  SecureHub.HubRoute,
    authRoutePattern: SecureHub.AuthRoute,
    authenticate:     SecureHub.AuthenticateAsync);
```

```csharp
// Client
var client = new SecureHubClientWithJWTAuth("https://localhost:7159");

var auth = await client.AuthorizeAsync("valid-credentials");
if (!auth.IsAccepted)
{
    Console.WriteLine($"Auth rejected: {auth.RejectionReason}");
    return;
}

await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello secure world");
```

`AuthorizeAsync` returns a `G9DtAuthorizeResult` with `IsAccepted`, `JWToken`, `RejectionReason`, and `ExtraData`. The token is cached in the client and used automatically on `ConnectAsync()`. To pass a token explicitly: `await client.ConnectAsync(jwToken)`.

## Sample with sessions

`G9AHubBaseWithSession<THub, TClient, TSession>` adds a thread-safe per-user session keyed by user identifier (or connection id when there is no user). Multiple connections from the same user share one session and a connection counter.

```csharp
public class ChatSession : G9ASession
{
    public int MessagesSent { get; set; }
    public string? DisplayName { get; set; }
}

public class ChatHubWithSession :
    G9AHubBaseWithSession<ChatHubWithSession, IChatClient, ChatSession>
{
    public override string RoutePattern() => "/chat-session";

    public Task SendMessage(string message)
    {
        Interlocked.Increment(ref _messagesSent);
        return Clients.All.ReceiveMessage(Session.DisplayName ?? "anon", message);
    }

    public bool IsOnline(string userId) => IsUserConnected(userId);
}
```

Register the session store once during startup:

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddG9SignalRSuperNetCoreSessionStore<ChatSession>();
app.AddSignalRSuperNetCoreServerHub<ChatHubWithSession, IChatClient>("/chat-session");
```

> Custom fields you add are not automatically thread-safe. The framework manages `ConnectionCounts` and `LastActivityDateTime` atomically; for your own counters use `Interlocked` or a lock.

You can call `CleanupExpiredSessions(TimeSpan.FromMinutes(30))` from a background job to evict idle sessions.

## Sample with JWT + sessions (recommended)

For most production hubs, you want both. `G9AHubBaseWithSessionAndJWTAuth` combines them, and the auth user identifier is used as the session key automatically.

```csharp
public class AppHub :
    G9AHubBaseWithSessionAndJWTAuth<AppHub, IChatClient, ChatSession>
{
    public const string HubRoute = "/SecureHub";
    public const string AuthRoute = "/AuthHub";

    public static TokenValidationParameters TokenValidationParameters { get; } =
        BuildTokenTemplate().ValidationParameters!;

    public override string RoutePattern() => HubRoute;
    public override string AuthAndGetJWTRoutePattern() => AuthRoute;

    public static Task<(G9JWTokenFactory, object?)> AuthenticateAsync(
        object authorizeData, Hub authHub) => /* your credential check */;

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub authHub) => AuthenticateAsync(authorizeData, authHub);

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub()
        => TokenValidationParameters;

    public Task<List<string>> GetRecentMessages()
    {
        Session.MessagesSent++;
        return Task.FromResult(new List<string> { "msg1", "msg2" });
    }

    private static G9JWTokenFactory BuildTokenTemplate() =>
        G9JWTokenFactory.GenerateJWTToken("replace-me", "G9TM", "G9TM", DateTime.UtcNow.AddDays(3));
}
```

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddG9SignalRSuperNetCoreSessionStore<ChatSession>();
builder.Services.AddSignalRSuperNetCoreJwt(AppHub.HubRoute, AppHub.TokenValidationParameters);

var app = builder.Build();

app.AddSignalRSuperNetCoreJwtHub<AppHub, IChatClient>(
    hubRoutePattern:  AppHub.HubRoute,
    authRoutePattern: AppHub.AuthRoute,
    authenticate:     AppHub.AuthenticateAsync);
```

---

## Pluggable session store

Session storage is exposed as `IG9SessionStore<TSession>` and resolved through DI. The default registration uses `G9CInMemorySessionStore<TSession>`, which is thread-safe and zero-config:

```csharp
builder.Services.AddG9SignalRSuperNetCoreSessionStore<ChatSession>();
```

The contract:

```csharp
public interface IG9SessionStore<TSession> where TSession : G9ASession, new()
{
    ValueTask<TSession> GetOrCreateAsync(string sessionId, Func<TSession> factory, CancellationToken ct = default);
    ValueTask<int>      ReleaseAsync   (string sessionId, CancellationToken ct = default);
    bool                TryGet         (string sessionId, out TSession? session);
    bool                IsConnected    (string sessionId);
    int                 CleanupExpiredSessions(TimeSpan threshold);
}
```

`GetOrCreateAsync` atomically creates a session on first connection and increments the connection counter. `ReleaseAsync` atomically decrements and removes the session when the counter hits zero. The in-memory implementation is lock-free; counters use `Interlocked`.

For horizontal scale-out, replace the registration with your own distributed implementation of `IG9SessionStore<TSession>`. A Redis-backed store is planned for a separate package, so the core library keeps no Redis dependency; it has not shipped yet (see [Roadmap](#roadmap)).

## Auto-generated typed client

The library ships a Roslyn `IIncrementalGenerator` (`G9SignalRSuperNetCore.SourceGenerator`) that scans every project that consumes the library, finds your hubs, and emits a strongly-typed, AOT-safe client for each one. No reflection, no `MethodInfo.Invoke`, no Castle DynamicProxy.

### How to wire it up

The generator ships **embedded inside the `G9SignalRSuperNetCore.Server` package** under `analyzers/dotnet/cs`. Three project layouts are supported:

**Layout A — single project** (rare; you can't do this if your client is a different platform than your server):

```xml
<ItemGroup>
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.10.0" />
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.10.0" />
</ItemGroup>
```

The generator runs on the project that defines the hub, emits the client into the same compilation, and you get `Server`, `IChatHubMethods`, `IChatHubListeners`, `ChatHubClient` all in one assembly.

**Layout B — separate Server / Client projects with a Shared library** (recommended):

```
MyApp.Shared/        // referenced by both Server and Client
  ChatHub.cs         // your hub class
  IChatClient.cs     // your listener interface

MyApp.Server/        // ASP.NET Core process
  Program.cs

MyApp.Client/        // console / MAUI / WPF / Blazor
  Program.cs
```

```xml
<!-- MyApp.Shared.csproj -->
<ItemGroup>
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.10.0" />
</ItemGroup>

<!-- MyApp.Server.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MyApp.Shared\MyApp.Shared.csproj" />
</ItemGroup>

<!-- MyApp.Client.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MyApp.Shared\MyApp.Shared.csproj" />
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.10.0" />
</ItemGroup>
```

The generator runs in `MyApp.Shared` (because that's where the hub class lives) and the typed `ChatHubClient` is emitted into `MyApp.Shared`'s compilation. `MyApp.Client` references the Shared library and gets the typed client for free.

**Layout C — generator only on the client side** (when the client project doesn't reference the shared library):

```xml
<!-- MyApp.Client.csproj -->
<ItemGroup>
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.10.0"
                    PrivateAssets="all" />        <!-- only the analyzer is needed -->
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.10.0" />
</ItemGroup>
```

`PrivateAssets="all"` keeps the analyzer (the generator) reachable but suppresses the runtime Server assembly so your client doesn't accidentally pull ASP.NET Core into a MAUI app.

### What it generates

For a hub like:

```csharp
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    public const string Route = "/chat";
    public override string RoutePattern() => Route;

    /// <summary>Sends a message to everyone in the room.</summary>
    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);

    public Task<List<string>> GetRecentMessages() => /* ... */;
}

public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
    Task UserJoined(string user);
}
```

the generator emits (into `obj/Generated/G9SignalRSuperNetCore.SourceGenerator/.../ChatHub.g.cs`):

- `IChatHubMethods` — methods interface mirroring the hub. `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, `IAsyncEnumerable<T>`, and `CancellationToken` parameters are all supported.
- `IChatHubListeners` — listener interface mirroring `IChatClient`.
- `ChatHubClient` — partial class wired to `serverUrl + "/chat"`, with the typed `Server` proxy and default no-op listener overrides.
- `ChatHubServerProxy` — internal sealed class that turns each method on `IChatHubMethods` into a single `HubConnection.InvokeCoreAsync` / `InvokeCoreAsync<T>` / `StreamAsyncCore<T>` call (`SendCoreAsync` for a method marked `[G9AttrOneWay]`).

XML doc comments on hub methods and on the listener interface are forwarded into the generated code, so IntelliSense shows your descriptions on the client side.

### How to use it

Subclass the generated `{HubName}Client` and override the listener methods you care about:

```csharp
var client = new MyChatClient("https://localhost:7159");
await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello");
List<string> recent = await client.Server.GetRecentMessages();

public sealed class MyChatClient(string url) : ChatHubClient(url)
{
    public override Task ReceiveMessage(string user, string message)
    {
        Console.WriteLine($"[{user}] {message}");
        return Task.CompletedTask;
    }

    public override Task UserJoined(string user)
    {
        Console.WriteLine(user + " joined");
        return Task.CompletedTask;
    }
}
```

### Knobs and escape hatches

Exclude a hub method from generation:

```csharp
[G9AttrExcludeFromClientGeneration]
public Task InternalDiagnostic() => Task.CompletedTask;
```

Deny a method by policy at the auth layer:

```csharp
[G9AttrDenyAccess]
public Task DangerousAction() => Task.CompletedTask;
```

Inspect the generated source in your IDE: set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` in the consuming `.csproj`. The output appears under `obj/Generated/G9SignalRSuperNetCore.SourceGenerator/G9SignalRSuperNetCore.SourceGenerator.G9HubClientGenerator/`.

### Diagnostics

The generator publishes stable diagnostic IDs so the IDE highlights bad hub signatures at design time:

| ID | Severity | Meaning |
|---|---|---|
| `G9001` | Warning | Hub method returns an unsupported type. Use `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, or `IAsyncEnumerable<T>`. |
| `G9002` | Error | Listener method returns something other than `Task` / `ValueTask`. |
| `G9003` | Warning | Hub method has more than 16 parameters (SignalR limit). |
| `G9004` | Warning | Generic hub methods are not supported. |
| `G9005` | Info | Hub class is missing a `RoutePattern()` override. |
| `G9006` | Warning | Hub is missing the listener interface generic argument. |
| `G9007` | Error | Internal generator error — please file an issue with the diagnostic message. |

The generator runs incrementally, so editing your hub updates the typed client on the next keystroke without a full rebuild.

---

## Client features

### Typed server proxy

```csharp
await client.Server.SendMessage("Iman", "Hi");
List<string> result = await client.Server.GetRecentMessages();

// Streaming — generated from IAsyncEnumerable<T> hub method:
await foreach (var item in client.Server.SubscribePrices("AAPL"))
    Console.WriteLine(item);
```

Each method on the server interface maps to a single typed call on the underlying `HubConnection`. `Task` / `ValueTask` methods use `InvokeCoreAsync`, so awaiting them waits for the server (2.7+), or `SendCoreAsync` when the hub method carries `[G9AttrOneWay]`; `Task<T>` methods use `InvokeCoreAsync<T>`; `IAsyncEnumerable<T>` uses `StreamAsyncCore<T>`. No `MethodInfo.Invoke`, no boxing of return values.

### Listener wiring

The generated client overrides `RegisterListenerMethods()` and registers each listener with a typed `Connection.On<...>(...)` overload. You override the handler methods in your subclass:

```csharp
public sealed class MyClient(string url) : ChatHubClient(url)
{
    public override Task ReceiveMessage(string user, string msg)
    {
        Console.WriteLine($"[{user}] {msg}");
        return Task.CompletedTask;
    }
}
```

### Automatic reconnect and timeouts

Automatic reconnect is enabled by default with [`G9CClientReconnectPolicy`](#resilient-client-reconnect), and the server timeout is 60 seconds. Both can be customized through the `customConfigureBuilder` and `configureHttpConnection` constructor parameters of the generated client.

### Opt-in connection behaviours (`ConfigureConnectionOptions`)

Two behaviours are off unless the client turns them on (2.8+): [stateful reconnect](#stateful-reconnect-opt-in) and [WebSockets first](#websockets-first-with-a-safe-fallback-opt-in). Both are set in one place, a virtual hook next to `RegisterListenerMethods()`:

```csharp
public partial class ChatHubClient   // the generated client is partial; a hand-written client overrides it the same way
{
    protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options)
    {
        options.UseStatefulReconnect = true;   // or: options.WebSocketsFirst = true;
    }
}
```

The base class calls it every time it builds the connection, before your `customConfigureBuilder` callback. For a client that passes its URL to the base constructor — every generated non-JWT client — that is *inside the base constructor*, before your constructor body runs, exactly like `RegisterListenerMethods()`. Read constants, statics or field initializers there, not fields your constructor assigns. A client that builds its connection later (the JWT client does, on `ConnectAsync`; so does a hand-written client that calls `PrepareConnection` from its own constructor) can read its own fields.

### Stateful reconnect (opt-in)

With an ordinary connection, a network break of a second costs the connection: SignalR's automatic reconnect opens a *new* one with a new connection id, the hub runs `OnDisconnectedAsync` and `OnConnectedAsync`, and whatever was in flight is gone. With stateful reconnect (ASP.NET Core 8+), both sides buffer what they sent, the client reconnects the transport under the *same* connection id, and each replays what the other missed.

| Side | Turn it on |
|---|---|
| Server endpoint | `app.AddSignalRSuperNetCoreServerHub<THub, TClient>(route, allowStatefulReconnects: true)` — also on `AddSignalRSuperNetCoreJwtHub` (protected hub only) |
| Server buffer (optional) | `services.AddG9SignalRSuperNetCoreStatefulReconnect(bufferSizeBytes)` — per connection; SignalR's default is 100,000 |
| Client | `options.UseStatefulReconnect = true` in `ConfigureConnectionOptions`; optional `options.StatefulReconnectBufferSize` |

- It happens only when **both** sides opted in. A client that asks on an endpoint that does not allow it, or the other way round, gets an ordinary connection.
- It works over JSON and over MessagePack, and on both client targets (net10.0 and netstandard2.1).
- The buffers are memory: the server keeps up to `bufferSizeBytes` *per connection that negotiated it*, and stops sending on a connection whose buffer is full until the client acknowledges. Size it for your message sizes, and multiply by your connection count.
- A resumed connection fires **no** `Reconnecting` / `Reconnected` on the client and no `OnDisconnectedAsync` on the hub. Code that re-subscribes or re-sends state on `Reconnected` simply does not run — which is the point, but check that nothing depends on it as a heartbeat.
- It covers a break, not an outage: the client retries the transport at once and the server keeps a broken connection for seconds, not minutes. Anything longer falls through to the ordinary automatic reconnect with a new connection id, as before.
- It is agreed in the negotiate response, so it cannot be combined with skipping negotiation: with `WebSocketsFirst` also on, stateful reconnect wins and the client negotiates.

### WebSockets first, with a safe fallback (opt-in)

```csharp
protected override void ConfigureConnectionOptions(G9DtClientConnectionOptions options) =>
    options.WebSocketsFirst = true;
```

`ConnectAsync` then opens a WebSocket directly, without the negotiate request: one round trip less on every connect, and no sticky sessions needed when the server runs behind a load balancer. What normally makes `SkipNegotiation` risky is that it is all-or-nothing — on a network or proxy that blocks WebSockets the connect fails and nothing else is tried. Here the same `ConnectAsync` call tries once more with ordinary negotiation and the transports your `configureHttpConnection` callback left in place (all of them unless you narrowed them).

| First attempt fails with | Second attempt? |
|---|---|
| Refused or broken WebSocket upgrade (404/400/502 instead of 101, connection reset, handshake timeout, no WebSocket support on the platform) | **Yes**, with negotiation |
| 401 / 403 | No — the same credentials would be refused again |
| `HubException` from the hub handshake (e.g. a protocol the server does not offer) | No — the transport worked |
| Cancellation through the caller's token | No |
| The connection is disposed, or someone else is already starting it | No |

- `StateChanged` reports `Connecting` → `TransportFallback` (detail: the first failure) → `Connected` or `ConnectFailed`. If the second attempt fails too, **its** exception is thrown: the one a client without the option would have thrown.
- **The connection is not rebuilt.** The fallback switches `SkipNegotiation` and `Transports` on the `HttpConnectionOptions` the connection already has (SignalR copies them each time it opens a transport). `client.Connection` is the same instance before and after, so handlers you registered on it directly, `ServerTimeout` / `KeepAliveInterval`, and a `G9CFileUploader` built on it all survive.
- Every `ConnectAsync` starts with WebSockets again. A fallback caused by an outage, or by the network the device happened to be on, does not pin the client to negotiation.
- **What it does not cover:** SignalR's automatic-reconnect loop is its own and connects with the settings the connection settled on — direct WebSockets when that worked, negotiation after a fallback. A device that connected over WebSockets and then moves to a network that blocks them keeps failing its automatic reconnects until the reconnect budget is spent (5 minutes by default); the next `ConnectAsync` then falls back. If that matters more to you than the saved round trip, leave the option off: negotiation tries WebSockets first anyway.
- A 401 / 403 on a WebSocket upgrade is recognised from the exception message (`… status code '401' …`) because the client WebSocket exposes no status property there; on a runtime that words it differently, an authentication failure costs one extra (refused) negotiate request and then fails as before.
- With `UseStatefulReconnect` also on, this option is ignored (see above).

### Connection access

If you need to drop down to raw SignalR APIs, the underlying `HubConnection` is exposed via `client.Connection`:

```csharp
await foreach (var item in client.Connection.StreamAsync<int>("MyStream"))
    Console.WriteLine(item);
```

### `IAsyncDisposable`

The base client implements `IAsyncDisposable`. `await using var client = new ChatHubClient(url);` disposes the connection cleanly.

### Connection quality (2.9)

`G9CConnectionQualityMonitor` probes the built-in `G9Ping` hub method at an interval (default 5 s) and reports `Good` (< 150 ms), `Fair` (< 400 ms), `Poor` (≥ 400 ms, or one failed probe) or `Lost` (three failed probes in a row, or not connected). `QualityChanged` fires only when the level changes, and `Current` holds the latest measurement. Keep the interval at 500 ms or more, because the server rate-limits `G9Ping` to 2 calls a second per connection. It works on both client targets (net10.0 and netstandard2.1) and needs a 2.9 server; against an older one every probe fails and the level ends at `Lost`.

```csharp
await using var quality = new G9CConnectionQualityMonitor(client.Connection, TimeSpan.FromSeconds(5));
quality.QualityChanged += q => Console.WriteLine($"{q.Level} ({q.RttMs:F0} ms)");
quality.Start();
```

### Waiting for the connection (2.9)

`connection.WaitUntilConnectedAsync(timeout, ct)` returns at once when the connection is `Connected`. Otherwise it waits for the automatic reconnect to finish and throws `TimeoutException` after `timeout` (`Timeout.InfiniteTimeSpan` waits until `ct` is cancelled). It never starts the connection itself. `G9CFileUploader` calls it before each `BeginUpload` with its `ReconnectGrace`.

---

## TypeScript client (parity rule)

A TypeScript twin of the .NET client lives in [`js/`](https://github.com/ImanKari/G9SignalRSuperNetCore/tree/main/js) of this repository, an npm workspace with two packages, both versioned with the NuGet packages (2.10.0):

| Package | What | Runs on |
|---|---|---|
| **`@g9tm/signalr-supernetcore-client`** ([`js/packages/client`](https://github.com/ImanKari/G9SignalRSuperNetCore/tree/main/js/packages/client)) | The twin of `G9SignalRSuperNetCore.Client`: `G9Client`, `authorize`, the resumable uploader and downloader, the quality monitor, error codes. `./node` adds `nodeFileSystem` | browsers, WebViews, Node 18+, **Lynx** |
| **`@g9tm/signalr-supernetcore-lynx`** ([`js/packages/lynx`](https://github.com/ImanKari/G9SignalRSuperNetCore/tree/main/js/packages/lynx)) | The Lynx platform layer: the `G9SignalRLynxModule` native module (binary WebSocket + files; Android/iOS via Autolink, desktop via a Lynxtron preload), `createLynxClient`, `lynxFileSystem`, a `URL` shim | Lynx (Android, iOS, Windows, macOS, Web) |

It calls the same hub methods and uses the same DTO member names and error codes as the .NET client. It ships as ESM with type declarations and has one peer dependency, `@microsoft/signalr` ^10.

**Install.** The release pipeline type-checks, tests, builds and packs both packages with every release, and publishes them to npm when the `NpmToken` secret is configured:

```bash
npm install @microsoft/signalr@^10 @g9tm/signalr-supernetcore-client@2.10.0
npm install @g9tm/signalr-supernetcore-lynx@2.10.0             # Lynx apps
npm install @microsoft/signalr-protocol-msgpack@^10             # optional, binary frames

# or from the tarballs: in js/, `npm run pack:all` writes both to js/artifacts/npm/
# (`npm run pack:vendor -w packages/client` copies the client's into the G9Hub web app's vendor/ folder)
```

```ts
import { authorize, G9Client } from '@g9tm/signalr-supernetcore-client';

const auth = await authorize('https://host/AuthHub', 'valid-credentials');   // a refusal is a result, not an exception
if (!auth.isAccepted) throw new Error(auth.rejectionReason ?? 'refused');       // 'G9_RATE_LIMITED' when throttled (2.9)
const client = new G9Client({ url: 'https://host/SecureHub', accessTokenFactory: () => auth.jwToken! });
await client.start();
await client.invoke('SendMessage', 'Iman', 'Hello from the browser');
```

The [client README](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/js/packages/client/README.md) covers connecting, uploads, downloads and the quality monitor. [PARITY.md](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/js/packages/client/PARITY.md) maps every .NET member to its TypeScript twin and lists the deliberate differences.

### Lynx

```ts
import { createLynxClient, lynxAuthorize, lynxFileSystem } from '@g9tm/signalr-supernetcore-lynx';

const auth = await lynxAuthorize('https://host/AuthHub', credentials);
const client = createLynxClient({ url: 'https://host/SecureHub', accessTokenFactory: () => auth.jwToken! });
await client.start();
```

- **Android:** Autolink links the module from `node_modules` (`lynx.lib.json`); it brings OkHttp 4.12 and the `INTERNET` permission.
- **iOS:** CocoaPods Autolink adds the `g9tm-signalr-lynx` pod; register the module on the `LynxConfig`: `[config registerModule:G9SignalRLynxModule.class]` (Lynx 4.0.3 has no self-registration).
- **Windows/macOS (Lynxtron):** in the preload, `contextBridge.exposeInLynxBTS({ g9signalr: createG9SignalRBridge(), …other APIs })` from `@g9tm/signalr-supernetcore-lynx/lynxtron`.

The [Lynx README](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/js/packages/lynx/README.md) has the full host setup, the transport choices and what a device run still has to prove.

### The rule

**Every change to the public surface of `G9SignalRSuperNetCore.Client` must be made in the TypeScript package in the same change, and vice versa — and a change to the native module contract (`js/packages/lynx/types/g9-signalr-lynx-module.d.ts`) is made on Android, iOS and the Lynxtron bridge in the same change.** That covers methods, options, reconnect behaviour, error codes and the file-transfer DTOs. A pull request that changes only one side is incomplete. The same applies to the server contract every client depends on: hub method names (`BeginUpload`, `UploadChunks`, `BeginDownload`, `DownloadChunks`, `G9Ping`, `Authorize` / `AuthorizeResult`), DTO member names (they are the wire format), and the `G9_*` codes. The repository's [AGENTS.md](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/AGENTS.md) spells out the checklist.

| .NET | TypeScript (`@g9tm/signalr-supernetcore-client`) | What must match |
|---|---|---|
| `G9SignalRSuperNetCoreClient` | `G9Client` | One connection per client, the stateful-reconnect options and the state reporting. The defaults differ on purpose: the TypeScript client retries the first connect, restarts after a final close and turns stateful reconnect on (see PARITY.md). |
| `G9CClientReconnectPolicy` | `G9ReconnectPolicy` | `G9ReconnectPolicy.exponential()` reproduces the .NET curve: 200 ms base, ×2 per attempt, 30 s cap, ±15% jitter, 5-minute budget. `fromDelegate` is the custom-delegate escape hatch. The TypeScript client's default policy is a schedule that never gives up. |
| `G9CFileUploader` | `G9FileUploader` | The `BeginUpload` / `UploadChunks` protocol, the upload id (`uploadFile(path)` computes the .NET one; a Blob or byte source uses `name\|size\|lastModified`), the SHA-256 declaration, resume from `BytesAlreadyReceived`, waiting up to `ReconnectGrace` (2 min) for the connection before `BeginUpload` (2.9), retries with backoff, server-acknowledged progress, and `StoredFileName` on the result (2.9). |
| `G9CFileDownloader` | `G9FileDownloader` | The `BeginDownload` / `DownloadChunks` protocol, resume (`downloadToFile` from a `.partial` file exactly like .NET; `download`/`downloadBytes` from the bytes already received), and SHA-256 verification before the result is handed over. |
| `WaitUntilConnectedAsync` (2.9) | `waitUntilConnected()` / `client.waitUntilConnected()` | Returns at once when connected; otherwise completes on a reconnect or a 100 ms state poll, and times out. Never starts the connection. |
| `G9CConnectionQualityMonitor` | `G9ConnectionQualityMonitor` | Probing `G9Ping` every 5 s by default. Good < 150 ms, Fair < 400 ms, Poor at ≥ 400 ms or after 1 failed probe, Lost after 3 failures in a row or whenever the connection is not connected. `RttMs = -1` without a measurement; the change event fires only when the level changes. |
| `G9CErrorCodes` (server constants) | `G9ErrorCodes` | The same `G9_*` strings, including 2.9's `G9_PERMISSION_REQUIRED` and `G9_UPLOAD_FORBIDDEN`. |
| JWT authorize (`AuthorizeAsync` on `G9SignalRSuperNetCoreClientWithJWTAuth`) | `authorize()` | Sending `Authorize(payload)` on the auth route and resolving with the `AuthorizeResult` callback (`IsAccepted`, `RejectionReason`, `JWToken`, `ExtraData`). That includes the `G9_RATE_LIMITED` rejection from the 2.9 throttle. |

Public surface that is not in the table follows the same rule under the TypeScript naming conventions. The TypeScript tests (`js/packages/client/test/parity.test.ts`) read the .NET sources and fail when the error codes, the file-transfer and authorize DTO member names, the upload status values or the quality thresholds drift apart; in CI a missing source folder is a failure, not a skip. `js/scripts/check-versions.mjs` keeps the versions in step.

## Policy attributes

All attributes are opt-in. `[G9AttrConnectionLimit]` goes on the hub class; rate limits and the connection check go on methods; role, claim, permission and telemetry attributes work on a method or on the hub class (2.9: the class and its bases apply to every method, including the built-in `G9Ping`). A class-level role set and a method-level role set must both pass; every claim and permission is required wherever it is declared. The hub filter (`G9CHubFilter`, registered automatically by `AddSignalRSuperNetCoreCore()`) reads them from a per-method `MethodMeta` cache, so methods with **no** G9 attributes pay only the cost of one dictionary lookup per call.

| Attribute | Target | Rejected with | Notes |
|---|---|---|---|
| `[G9AttrRateLimit(perSecond, burst)]` | method | `G9_RATE_LIMITED` | Lock-free token bucket per `(connectionId, method)`. Bursts up to `burst`, refills at `perSecond`. With `Scope = G9ERateLimitScope.User` or `.Ip` (2.9), one bucket per `(user, method)` or `(IP, method)` is shared by all of that user's or address's connections. |
| `[G9AttrConnectionLimit(perUser, perIp)]` | class | `G9_CONNECTION_LIMIT` | Caps simultaneous connections. `0` disables that dimension. Enforced on connect, before `OnConnectedAsync`. Counted per hub type (2.9). Each slot is given back exactly once: at disconnect, after an abrupt close too, or when the hub's own `OnConnectedAsync` throws. |
| `[G9AttrConnectionRequired]` | method | `G9_CONNECTION_REQUIRED` | Fails fast when `Context.ConnectionAborted` is already cancelled. |
| `[G9AttrRequireRole("admin")]` | method or class | `G9_ROLE_REQUIRED` | Caller must carry at least one of the listed roles. Stackable; stacked attributes on one member add to the accepted roles (any one of them passes). A class-level set is a separate gate: class and method sets must both pass. |
| `[G9AttrRequireClaim("scope", "chat:write")]` | method or class | `G9_CLAIM_REQUIRED` | Caller must carry the claim. Empty `acceptedValues` accepts any value. Stackable; every claim on the method and its class is required. |
| `[G9AttrRequirePermission("rooms.moderate")]` (2.9) | method or class | `G9_PERMISSION_REQUIRED` | Decided by your `IG9HubPermissionHandler`. Every permission on the method and its class must be granted. Stackable. Refused when no handler is registered. |
| `[G9AttrTelemetry]` / `[G9AttrTelemetry("name")]` | method or class | n/a | Wraps invocation in an `ActivitySource` span tagging connection id, user id, and outcome (`ok` / `cancelled` / `faulted`; `stream_started` for a streaming method). `SampleRate = 0.1` (2.9) traces a fraction of the calls. |
| `[G9AttrDenyAccess]` (1.x carryover) | method | (auth pipeline rejects) | Applies a deny-by-default policy; framework methods use this. |
| `[G9AttrExcludeFromClientGeneration]` (1.x carryover) | method | n/a | Tells the source generator to skip the method. |

**Sample**:

```csharp
[G9AttrConnectionLimit(perUser: 5, perIp: 50)]
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    [G9AttrTelemetry]
    [G9AttrRateLimit(perSecond: 5, burst: 10)]
    public Task SendMessage(string user, string message) =>
        Clients.All.ReceiveMessage(user, message);

    [G9AttrRequireRole("admin")]
    [G9AttrTelemetry("admin.purge")]
    public Task PurgeHistory() => /* ... */;

    [G9AttrRequireClaim("scope", "chat:write")]
    public Task SendDirect(string toUser, string message) => /* ... */;
}
```

The client sees rejections as `HubException` whose message equals the stable error code, so you can switch on it cleanly:

```csharp
try { await client.Server.SendMessage(user, msg); }
catch (HubException ex) when (ex.Message.Contains("G9_RATE_LIMITED"))
{
    ShowToast("You're sending too fast — slow down a bit.");
}
catch (HubException ex) when (ex.Message.Contains("G9_CONNECTION_LIMIT"))
{
    ShowToast("Too many sessions open for your account.");
}
```

## Resumable file upload

Upload large files over SignalR with chunk streaming, server-acknowledged progress, and **safe resume on disconnect**. The pipeline is opt-in: register the service on the server and use `G9CFileUploader` on the client.

### Server side

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddG9SignalRSuperNetCoreFileUpload(opt =>
{
    opt.RootDirectory   = Path.Combine(builder.Environment.ContentRootPath, "uploads");
    opt.MaxBytes        = 5L * 1024 * 1024 * 1024; // 5 GB
    opt.PartialTtl      = TimeSpan.FromHours(24);  // abandoned partials are purged after 24h
    opt.AckEveryNChunks = 16;                       // server pushes UploadProgress every ~1 MB at 64 KB chunks
});
```

Add upload methods to your hub. The shape is two methods: `BeginUpload` to negotiate the resume offset, then `UploadChunks` for the streamed bytes:

```csharp
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    public async Task<G9DtBeginUploadResult> BeginUpload(
        string uploadId, string fileName, long totalBytes, int chunkSize, string declaredSha256Hex)
    {
        var svc = ResolveUploadService();   // IG9UploadService from DI
        try
        {
            // 2.9: the first argument is the owner (null = anonymous; the overload without it still exists)
            return await svc.BeginAsync(Context.UserIdentifier, uploadId, fileName, totalBytes, chunkSize,
                declaredSha256Hex, Context.ConnectionAborted);
        }
        catch (InvalidOperationException e) when (e.Message.StartsWith("G9_", StringComparison.Ordinal))
        {
            throw new HubException(e.Message);   // SignalR shows the client only a HubException's message
        }
    }

    public async Task<G9DtUploadResult> UploadChunks(string uploadId, IAsyncEnumerable<byte[]> chunks)
    {
        var svc    = ResolveUploadService();
        var caller = Clients.Caller;

        return await svc.AppendChunksAsync(
            Context.UserIdentifier,   // the same owner as in BeginUpload
            uploadId,
            chunks,
            onProgress: bytes => caller.UploadProgress(new G9DtUploadProgress
            {
                UploadId      = uploadId,
                BytesReceived = bytes,
                TotalBytes    = 0   // 0 means "use the total you already declared"
            }),
            Context.ConnectionAborted);
    }
}
```

Add `Task UploadProgress(G9DtUploadProgress progress)` to your `IChatClient` listener interface so the server-acknowledged progress flows back to the typed client.

**Where files end up**:

- In flight: `{RootDirectory}/{PartialSubdirectory}/{uploadId}.bin` (default `./uploads/.partial/`)
- Sidecar metadata: `{uploadId}.meta` (small JSON, source-generated serialization)
- After commit: atomically renamed to `{RootDirectory}/{fileName}` (timestamp-suffixed if the name collides). The result's `StoredFileName` (2.9) says which name was used.
- 2.9 options: `PerUserNamespace` stores an owner's partials under `{owner}-{hash}__{uploadId}`, `RandomizeCommittedNames` commits as `{32 hex}{.ext}`, `Authorize` can refuse any operation (`G9_UPLOAD_FORBIDDEN`), and `CleanupInterval` (default 10 minutes) runs the hosted sweep that deletes expired partials together with their `.meta`. Pass the owner with `BeginAsync(Context.UserIdentifier, …)` / `AppendChunksAsync(Context.UserIdentifier, …)`. See [2.9](#29--scoped-rate-limits-auth-route-throttle-upload-ownership-permissions-connection-index-and-quality).

The repo's `.gitignore` excludes `**/uploads/` and `**/.partial/` so committed files and partials never accidentally reach the repo.

### Client side

```csharp
using G9SignalRSuperNetCore.Client.FileUpload;

var uploader = new G9CFileUploader(client.Connection, new G9DtUploadClientOptions
{
    ChunkSize  = 64 * 1024,           // 64 KB; capped at 4 MB by the server
    MaxRetries = 5,                   // exponential backoff between retries
    ReconnectGrace = TimeSpan.FromMinutes(2), // 2.9: wait this long for a reconnect before each BeginUpload
    BeginMethod = "BeginUpload",      // hub method names; defaults shown
    UploadMethod = "UploadChunks"
});

var progress = new Progress<G9DtUploadClientProgress>(p =>
    Console.WriteLine($"{100.0 * p.BytesSent / p.TotalBytes:F1}%  " +
                      $"{p.BytesPerSecond / 1024.0:F0} KB/s  " +
                      $"acked {p.BytesAcknowledged}/{p.TotalBytes}"));

var result = await uploader.UploadAsync("file_upload_test.zip", progress, serverAckProgress: true, ct);

switch (result.Status)
{
    case G9EUploadStatus.Completed:
        Console.WriteLine($"OK — {result.BytesWritten} B stored as {result.StoredFileName}");   // 2.9; download it by this name
        Console.WriteLine($"     SHA-256 = {result.Sha256}");
        break;
    case G9EUploadStatus.Interrupted:
        // The partial is preserved server-side; calling UploadAsync again resumes from the new offset.
        Console.WriteLine($"Interrupted at {result.BytesWritten} B (resumable).");
        break;
    case G9EUploadStatus.Failed:
        Console.WriteLine($"FAIL — {result.ErrorCode}: {result.ErrorMessage}");
        break;
}
```

A refusal at `BeginUpload` (`G9_UPLOAD_FORBIDDEN`, `G9_UPLOAD_TOO_LARGE`, `G9_UPLOAD_NAME_CONFLICT`, `G9_UPLOAD_METADATA_CONFLICT`) is not a result: `UploadAsync` throws the `HubException`, whose message carries the code when the hub rethrows it as shown above. If the connection is not connected when an attempt starts, `UploadAsync` waits up to `ReconnectGrace` for it (2.9) and then throws `TimeoutException`.

### Resume guarantees

- **Append-only writes** server-side, so a partial file is never corrupted by retry.
- **Idempotent**: replaying the same chunk after a crash never duplicates bytes (server compares offset against current file length and refuses to over-write).
- **Tamper-detection**: the client's declared SHA-256 is verified before commit. Mismatch deletes the partial and returns `G9_UPLOAD_HASH_MISMATCH`.
- **Per-id concurrency** through a `SemaphoreSlim` so two parallel callers for the same `uploadId` never interleave bytes.
- **Cancellation-clean**: `OperationCanceledException` preserves the partial so a future call can resume.

### Out of scope (today)

- **Cross-server resume in a load-balanced cluster** — the partial lives on whichever server node first received chunks. With sticky sessions you're fine; without them, a future call may land on a different node and start over. The Bundle 5 Redis package and a shared blob backend will close this.
- **Bandwidth throttling / pause-resume from the UI** — easy to add on top; not built in.

## Resumable file download

Symmetric to the upload pipeline. The server exposes `BeginDownloadAsync(serverRelativePath, resumeFrom, chunkSize)` and a streaming `StreamFileAsync(...)`, each with an owner-aware overload (2.9) that passes the caller to the `Authorize` hook. The client uses `G9CFileDownloader` and gets the same resume guarantees.

```csharp
// Server hub methods (_uploads is the injected IG9UploadService; 2.9 passes the owner):
public Task<G9DtBeginDownloadResult> BeginDownload(string fileName, long resumeFrom, int chunkSize)
    => _uploads.BeginDownloadAsync(Context.UserIdentifier, fileName, resumeFrom, chunkSize, Context.ConnectionAborted).AsTask();

[G9AttrTelemetry]
public IAsyncEnumerable<byte[]> DownloadChunks(string fileName, long resumeFrom, int chunkSize, CancellationToken ct)
    => _uploads.StreamFileAsync(Context.UserIdentifier, fileName, resumeFrom, chunkSize, ct);

// Client:
var downloader = new G9CFileDownloader(connection, new G9DtDownloadClientOptions
{
    ChunkSize = 64 * 1024,
    OnRetry = info => Console.WriteLine($"resume from {info.BytesAlreadyOnServer} after {info.Backoff}")
});
var result = await downloader.DownloadAsync(
    serverFileName: "file_upload_test.zip",   // the upload's StoredFileName
    localTargetPath: @"C:\downloads\file_upload_test.zip",
    progress: new Progress<G9DtDownloadClientProgress>(p =>
        Console.WriteLine($"{100.0 * p.BytesReceived / p.TotalBytes:F1}%")));

if (result.Status == G9EUploadStatus.Completed) Console.WriteLine($"OK at {result.LocalPath}");
```

What you get for free:

- **Resume**: a `.partial` file on the client preserves bytes across cancels and disconnects; the next call seeks to the partial's length and continues.
- **Hash verification**: server sends its SHA-256 in `BeginDownload`; the client re-hashes before atomic rename.
- **Idempotent fast path**: if a fully-downloaded committed file already matches the server's hash, the call returns `Completed` without re-streaming.
- **Path-traversal safety**: server rejects `..`, absolute paths, and anything that escapes `RootDirectory`.
- **SHA cache**: the server caches the hash keyed by `(path, length, lastWriteUtc)` so it isn't recomputed on every begin call.

## Groups and presence

Two opt-in services that ride on top of SignalR's group machinery without losing the typed-proxy ergonomics.

```csharp
// Startup:
builder.Services.AddG9SignalRSuperNetCoreGroups<ChatHub>();
builder.Services.AddG9SignalRSuperNetCorePresence();

// Hub class:
[G9AttrPresenceTracked]               // emits G9DtPresenceEvents on Connect/Disconnect
[G9AttrAutoJoinGroup("lobby")]        // every new connection lands in "lobby"
public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    public Task JoinRoom(string roomName)
        => _groups.JoinAsync(Context.ConnectionId, roomName);

    public Task SendToRoom(string roomName, string user, string message)
        => Clients.Group(roomName).ReceiveMessage($"#{roomName} {user}", message);

    public List<string> ListRoomMembers(string roomName)
        => _groups.GetMembers(roomName).ToList();
}
```

`G9CPresenceTracker.Events` is a `ChannelReader<G9DtPresenceEvent>`. Drain it in a `BackgroundService` to fan out online/offline transitions to whichever clients you choose:

```csharp
public sealed class G9CPresenceBroadcastService : BackgroundService
{
    public G9CPresenceBroadcastService(G9CPresenceTracker tracker, IHubContext<ChatHub, IChatClient> hub) { ... }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var evt in _tracker.Events.ReadAllAsync(ct))
            await _hub.Clients.All.PresenceChanged(evt);
    }
}
```

Performance contract: hubs without `[G9AttrPresenceTracked]` pay no extra work. Auto-join is implemented as a hub-typed `G9CAutoJoinFilter<THub>` registered automatically by `AddG9SignalRSuperNetCoreGroups<THub>()` — AOT-safe, no `MakeGenericType` on the hot path.

## Server streams with backpressure

`G9CResilientStream.Create<T>(...)` returns a producer/consumer pair that completes the channel deterministically — no more "stream hangs because the writer was abandoned in a `catch`" footguns.

```csharp
[G9AttrTelemetry]
[G9AttrStreamBackpressure(capacity: 64, dropPolicy: G9EStreamDropPolicy.Wait)]
public IAsyncEnumerable<int> LiveTickerStream(int count, int intervalMs, CancellationToken ct)
{
    var attr = (G9AttrStreamBackpressureAttribute?)Attribute.GetCustomAttribute(
        typeof(ChatHub).GetMethod(nameof(LiveTickerStream))!, typeof(G9AttrStreamBackpressureAttribute));
    var (writer, reader) = G9CResilientStream.Create<int>(attr?.ToOptions());

    _ = Task.Run(async () =>
    {
        try
        {
            for (var i = 0; i < count && !ct.IsCancellationRequested; i++)
            {
                await writer.WriteAsync(i, ct);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* client unsubscribed */ }
        finally { writer.Complete(); }
    }, ct);

    return reader.AsAsyncEnumerable(ct);
}
```

The drop policies map onto `BoundedChannelFullMode`:

- `Wait` — block the producer until the consumer drains an item (default).
- `DropNewest` — drop the new item; older queued items survive.
- `DropOldest` — drop the oldest queued item to make room for the new one.

## Resilient client reconnect

`G9CClientReconnectPolicy` is wired into the generated client base by default. It keeps retrying with capped exponential backoff plus ±15% jitter and a configurable max-elapsed budget so a long outage doesn't trigger infinite retry storms.

```csharp
var conn = new HubConnectionBuilder()
    .WithUrl(url)
    .WithAutomaticReconnect(new G9CClientReconnectPolicy(
        baseDelay: TimeSpan.FromMilliseconds(200),
        factor:    2.0,
        maxDelay:  TimeSpan.FromSeconds(30),
        maxElapsed: TimeSpan.FromMinutes(5)))
    .Build();
```

Pass `Timeout.InfiniteTimeSpan` for `maxElapsed` to retry forever.

One policy instance can be shared by any number of connections: the jitter source is thread-safe from 2.8.0 (`Random.Shared` on net10.0, a locked `Random` on netstandard2.1).

## MessagePack hub protocol (opt-in)

Use it when payloads are large or binary: file transfer, sync pages, encrypted envelopes. The JSON protocol base64-encodes `byte[]` (+33%) and parses text. MessagePack sends bytes as bytes.

| Package | Adds |
|---|---|
| `G9SignalRSuperNetCore.Server.MessagePack` | `services.AddG9SignalRSuperNetCoreMessagePack(shapes)` / `signalRBuilder.AddG9MessagePackProtocol(shapes)`. JSON stays available. |
| `G9SignalRSuperNetCore.Client.MessagePack` | `hubConnectionBuilder.AddG9MessagePackProtocol(shapes)`, passed as a G9 client's `customConfigureBuilder` |

Both use [Nerdbank.MessagePack](https://aarnott.github.io/Nerdbank.MessagePack/docs/signalr.html), whose serializers are built from **type shapes generated at compile time** by [PolyType](https://github.com/eiriktsarpalis/PolyType). There is no reflection or dynamic code, so the protocol is NativeAOT- and trim-safe.

**1. Declare shapes** for every parameter, return and stream-item type of your hub methods and client callbacks. Do this once, in the shared assembly (it needs a `PolyType` package reference):

```csharp
using PolyType;

[GenerateShapeFor<ChatMessage>]
[GenerateShapeFor<List<string>>]
[GenerateShapeFor<string>]
[GenerateShapeFor<int>]
public sealed partial class ChatWireShapes;
```

The library's own hub types need no entry: the packages chain their shapes behind yours. `G9SignalRSuperNetCoreServerMessagePack.CombineShapes` / `G9SignalRSuperNetCoreClientMessagePack.CombineShapes` build that chain, and `LibraryShapes` is what they add. The built-in types are:
- the file-transfer results and acknowledgements;
- presence events (server);
- `byte[]`, `string`, `bool`, `int` and `long`.

**2. Server** — offer the protocol next to JSON:

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddG9SignalRSuperNetCoreMessagePack(ChatWireShapes.GeneratedTypeShapeProvider);
```

**3. Client** — opt in per connection:

```csharp
var client = new ChatHubClient(serverUrl,
    customConfigureBuilder: b => b.AddG9MessagePackProtocol(ChatWireShapes.GeneratedTypeShapeProvider));
```

Notes:

- **Missing shape:** a type without a shape fails when the first call using it is serialized, not at connect time. The test project shows how to assert that every type you exchange has a shape.
- **Server without MessagePack:** a MessagePack client connecting to a server that did not register it fails the handshake with `HubException: … The protocol 'messagepack' is not supported.`
- **JWT authentication:** keep the auth connection on JSON. `G9GetJwtHub.Authorize` takes an untyped `object`, and `G9DtAuthorizeResult.ExtraData` is an `object`; neither has a type shape. Pass the MessagePack builder as `customConfigureBuilder` only, not `customConfigureBuilderForAuthServer`.
- **Message size limits:** `HubOptions.MaximumReceiveMessageSize` counts protocol bytes, so the same limit admits ~33% larger binary arguments over MessagePack.
- **Target framework:** both packages target .NET 10 only. The netstandard2.1 (Unity) client build stays JSON-only.
- **Wire format:** it follows the [SignalR MessagePack spec](https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/HubProtocol.md#messagepack-msgpack-encoding). DTOs are encoded as maps keyed by property name, so client and server types only need matching member names (the file-transfer twins rely on this).

## App-level encryption (no TLS required)

For environments where you can't run TLS — internal networks, h2c reverse proxies, on-device IPC — `G9CHandshake` ships an ephemeral-static ECDH P-256 + HKDF-SHA-256 + ChaCha20-Poly1305 pipeline using only `System.Security.Cryptography` types.

```csharp
// Startup:
builder.Services.AddG9SignalRSuperNetCoreHandshake();
```

```csharp
// Server hub methods (in the sample ChatHub):
public Task<byte[]> GetServerPublicKey()              // 65-byte SEC1 pubkey
    => Task.FromResult(_handshake.StaticPublicKey.ToArray());

public Task<bool> BeginSession(byte[] ephemeralPub)   // caches the session key
{
    var key = _handshake.DeriveSessionKey(ephemeralPub);
    _sealer.SetSession(Context.ConnectionId, key);
    return Task.FromResult(true);
}

[G9AttrEncrypted]
public Task<byte[]> EncryptedEchoSession(byte[] envelope)
{
    var plaintext = _sealer.Open(Context.ConnectionId, envelope);
    return Task.FromResult(_sealer.Seal(Context.ConnectionId, plaintext));
}
```

```csharp
// Client side (one-shot per-call form):
var serverPub = await connection.InvokeAsync<byte[]>("GetServerPublicKey");
var (ephemeralPub, sessionKey) = G9CHandshake.ClientHandshake(serverPub);
var envelope = G9CHandshake.Seal(sessionKey, Encoding.UTF8.GetBytes("hello"));
var sealedReply = await connection.InvokeAsync<byte[]>("EncryptedEcho", ephemeralPub, envelope);
var plaintext = G9CHandshake.Open(sessionKey, sealedReply);
```

What you get:

- **Confidentiality + integrity** end-to-end against a network attacker.
- **Forward secrecy** through the per-session ephemeral keypair.
- **Server authentication** through the long-term static public key (TOFU; pin it on first connect, or hard-code for known deployments).
- **Constant-time, FIPS-validated, hardware-accelerated** (AES-NI / ARM CRYPTO).
- **AOT/MAUI-safe** — no third-party crypto dependencies.

What it isn't:

- Not a TLS replacement when you also need certificate revocation, downgrade protection, or origin authentication. Use TLS when you have the option.

Why P-256 instead of X25519 / Noise IK: the BCL ships `ECDiffieHellman` for NIST P-256 and `ChaCha20Poly1305`, but no managed X25519. Implementing X25519 by hand in a few hundred lines is a security footgun. The IETF-standard ECDHE-static handshake on P-256 gives equivalent security guarantees through vetted BCL code.

## Distributed backplane (interface)

`IG9DistributedBackplane` is a minimal publish/subscribe abstraction so the in-process group manager and presence tracker can be replaced with a Redis or NATS implementation when scaling out.

```csharp
builder.Services.AddG9SignalRSuperNetCoreBackplane();   // default: in-process no-op

// Or with a custom implementation:
builder.Services.AddG9SignalRSuperNetCoreBackplane(sp => new MyRedisBackplane(...));
```

The default `G9CInProcessBackplane` drops publishes (there are no other nodes to receive them) and returns an empty subscription. SignalR's own Redis backplane already handles message fan-out for hub invocations; this interface is for the G9-specific membership state.

## Telemetry, metrics, and stable error codes

`G9CTelemetry` exposes a single `ActivitySource` and a single `Meter` named `G9SignalRSuperNetCore`. Subscribe with OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing (t => t.AddSource("G9SignalRSuperNetCore"))
    .WithMetrics(m => m.AddMeter ("G9SignalRSuperNetCore"));
```

Built-in counters:

- `g9.signalr.rate_limited_invocations` — calls rejected by `[G9AttrRateLimit]`.
- `g9.signalr.connection_limit_rejections` — connections rejected by `[G9AttrConnectionLimit]`.
- `g9.signalr.authorization_rejections` — calls rejected by `[G9AttrRequireRole]` / `[G9AttrRequireClaim]` / `[G9AttrRequirePermission]`.

### Policy-rejection logging

In addition to the metrics above, `G9CHubFilter` emits a structured `Warning`-level log entry every time a policy refuses an invocation, so an operator reading the server log can see *why* a call or connect was rejected (not just that the client received a `HubException`). The filter resolves an `ILogger<G9CHubFilter>` from DI; when constructed outside DI (`new G9CHubFilter()`), logging routes to `NullLogger` and only the metrics fire. Four event ids:

- `9100` — per-invocation rejection (rate limit, role, claim, permission, connection-required). Fields: `ErrorCode`, `Method`, `ConnectionId`, `UserId`, `Detail`.
- `9101` — per-connect rejection (connection limit). Fields: `Hub`, `Dimension` (`per-user`/`per-ip`), `Key`, `Limit`.
- `9102` (2.9) — the JWT authorize route throttled a caller. Category `G9SignalRSuperNetCore.Server.Classes.Hubs.G9GetJwtHub`. Fields: `Route`, `RemoteIp`.
- `9103` (2.9) — a method requires a permission but no `IG9HubPermissionHandler` is registered, so the call was refused. Fields: `Method`, `Permissions`, `ConnectionId`.

Filter these out by raising the minimum level for the `G9SignalRSuperNetCore.Server.Classes.Filters.G9CHubFilter` category (and `G9SignalRSuperNetCore.Server.Classes.Hubs.G9GetJwtHub` for 9102) if the warnings are noisy in your environment. Refused file-transfer operations (2.9) are logged as warnings by `G9CUploadService`, without an event id.

Stable error codes (`G9SignalRSuperNetCore.Server.Classes.Errors.G9CErrorCodes`):

```
G9_RATE_LIMITED          G9_CONNECTION_LIMIT       G9_CONNECTION_REQUIRED
G9_ROLE_REQUIRED         G9_CLAIM_REQUIRED         G9_PERMISSION_REQUIRED (2.9)
G9_UPLOAD_TOO_LARGE      G9_UPLOAD_HASH_MISMATCH   G9_UPLOAD_UNKNOWN_ID    G9_UPLOAD_FAILED
G9_UPLOAD_NAME_CONFLICT  G9_UPLOAD_METADATA_CONFLICT                       G9_UPLOAD_FORBIDDEN (2.9)
```

The codes are part of the public API; switching on them in the client is supported and recommended.

---

## JWT helper

`G9JWTokenFactory` covers the common token shapes:

```csharp
// Username + role
var token = G9JWTokenFactory.GenerateJWTToken(
    jwtSecret: secret,
    username:  "Iman",
    role:      "admin",
    issuer:    "G9TM",
    audience:  "G9TM",
    expires:   DateTime.UtcNow.AddHours(8));

// Custom claim list
var token2 = G9JWTokenFactory.GenerateJWTToken(secret, "G9TM", "G9TM",
    new[] { new Claim("plan", "pro") },
    expires: DateTime.UtcNow.AddDays(1));

// Reject the request
var rejected = G9JWTokenFactory.RejectAuthorize("Invalid credentials");
```

A single `JwtSecurityTokenHandler` instance is reused process-wide.

Supported algorithms (`G9ESecurityAlgorithms`): `HmacSha256/384/512`, `RsaSha256/384/512`, `RsaSsaPssSha256/384/512`, `EcdsaSha256/384/512`, `Aes128/192/256KW`, `RsaOAEP`, `Rsa1_5`, `None`.

---

## Scaling to many connections

A single ASP.NET Core SignalR server typically handles up to ~100,000 WebSocket connections per process under proper tuning. To exceed that, scale out across servers:

1. **Pick a backplane.** For most teams the simplest answer is **Azure SignalR Service**; for self-hosted clusters in the same data centre, a **Redis backplane** is fine. Both are first-class ASP.NET Core SignalR scale-out targets and compose cleanly with this library.
2. **Sticky sessions.** With Redis, configure your load balancer for session affinity by `connectionId`. With Azure SignalR Service, the service handles affinity for you.
3. **Tune the OS.** On Linux: raise `ulimit -n`, increase `net.core.somaxconn`, expand the ephemeral port range, and ensure ports are not exhausted by short-lived backplane connections.
4. **Tune Kestrel.** Set `KestrelServerLimits.MaxConcurrentConnections` and `MaxConcurrentUpgradedConnections` in line with your hardware.
5. **Move session state out of process.** This is what `IG9SessionStore<TSession>` is designed for. The default in-memory store works for a single process; for multi-process clusters, swap in a distributed implementation.

This library does not embed any correctness-critical state in static fields. The same hub source code that runs in a single dev process runs unchanged behind a backplane.

The policy state is per process, though: rate-limit buckets (all three scopes), connection-limit counters, the JWT authorize throttle and `IG9UserConnectionIndex` (2.9) each see only the connections of their own node. Behind a load balancer, a user's allowance is therefore per node, and `AbortUser` reaches only the connections on the node where it runs.

A coming **G9SignalRSuperNetCore.Server.Redis** package will provide a turnkey distributed `IG9SessionStore<TSession>` plus an `AddG9SignalRBackplane(...)` helper that wires `Microsoft.AspNetCore.SignalR.StackExchangeRedis`. The same package will offer a Redis-backed token-bucket rate limiter so the policy attributes can enforce cluster-wide caps.

## Thread safety contract

The library is designed for high-concurrency hubs. Specifically:

- **Sessions are linearizable on connect/disconnect.** `G9ASession.ConnectionCounts` is mutated only through `Interlocked.Increment` / `Interlocked.Decrement`. Concurrent reconnect storms from the same user yield a final count equal to (#connects − #disconnects), and the counter is never negative at any observable point.
- **`LastActivityDateTime` never tears.** It is read and written through `Interlocked.Read` / `Interlocked.Exchange` on the underlying `long` ticks. You can read it from any thread and never see a partially-written `DateTime`.
- **Session removal is reference-equality safe.** A session is removed from the in-memory store only if no other connect has raced in and replaced the instance.
- **JWT route registry is read-mostly.** Registrations occur during startup; reads on every authorize call are lock-free `ConcurrentDictionary` lookups.
- **`G9JWTokenFactory` is process-shared.** A single `JwtSecurityTokenHandler` is reused; the handler is documented as thread-safe for issuance and validation.
- **Token bucket is wait-free in the uncontended case.** State is encoded in a single 64-bit field and updated through `Interlocked.CompareExchange` with bounded retry. No locks, no allocations on the hot path.
- **Connection counter cells are reference-counted.** A cell is removed from the dictionary only when its counter falls back to zero, so the dictionary doesn't grow unboundedly under churn.
- **Connection-limit slots and shared rate-limit buckets are released exactly once (2.9).** The filter records at connect which counters a connection took and gives exactly those back, without reading the (possibly disposed) HTTP context again. A user- or IP-scoped bucket is freed by the last connection that shares it, and a connect racing that last disconnect never increments a cell that is being removed.
- **File-upload writes are serialized per `uploadId`** through a `SemaphoreSlim`. Two racing append calls for the same upload never interleave bytes; two calls for *different* uploads run in parallel.
- **`G9CClientReconnectPolicy` is shareable.** `NextRetryDelay` may be called from any number of reconnect loops at once; from 2.8.0 its jitter comes from a thread-safe source, so the ±15% band holds under contention.

Custom fields you add to your `TSession` subclass are NOT automatically thread-safe. Synchronize them yourself with `Interlocked` or a lock.

## Console test harness (Consolonia)

The `G9SignalRSuperNetCore.ConsoleClient` sample is a Consolonia (Avalonia-for-the-terminal) UI that exercises every Bundle 2 feature end-to-end against the WebServer sample.

**Tabs**:

1. **Connection** — server URL, display name, Connect / Disconnect, connection log (joins/leaves, errors).
2. **Chat** — Send a message (default text: `G9SignalRSuperNetCore`), live receive log driven by the source-generated `ChatHubClient.ReceiveMessage` override.
3. **Recent** — Fetch the recent-messages list with `Server.GetRecentMessages()`.
4. **File upload** — Pick a path (default: `file_upload_test.zip`), choose chunk size, watch live percent + KB/s + acked-bytes update via `IProgress<G9DtUploadClientProgress>`. A Cancel button preserves the partial; the next click of Upload resumes.
5. **Rate limit** — Fires 100 `SendMessage` calls in a tight loop. The hub method is decorated with `[G9AttrRateLimit(perSecond: 5, burst: 10)]` so you can watch the burst go through, then `G9_RATE_LIMITED` rejections.

Run side-by-side:

```powershell
# Terminal 1
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.WebServer -c Release

# Terminal 2
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.ConsoleClient -c Release
```

Start the client with `--messagepack` (or set `G9_SAMPLE_PROTOCOL=messagepack`) to connect over the binary protocol; the sample server offers both.

The console app uses `Consolonia.UseAutoDetectedConsole()` so it works in Windows Terminal, ConEmu, iTerm2, GNOME Terminal, Alacritty, etc.

---

## Build and test

```powershell
# Restore + Release build the whole solution
dotnet build G9SignalRSuperNetCore/G9SignalRSuperNetCore.sln -c Release

# Run the tests (real Kestrel server on loopback, typed client, JSON and MessagePack)
dotnet test G9SignalRSuperNetCore/G9SignalRSuperNetCore.Tests -c Release

# Run the sample web server
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.WebServer -c Release

# Run the Consolonia console test harness (in another terminal; add -- --messagepack for the binary protocol,
# and --stateful-reconnect / --websockets-first after the same -- for the 2.8 connection options)
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.ConsoleClient -c Release
```

```bash
# The TypeScript and Lynx packages (from js/)
npm ci
npm run verify                                   # versions, typecheck, tests, Lynx-sandbox tests, build, Lynx runtime gate
npm run test:interop -w packages/lynx            # the Lynx-shaped sandbox against the real sample server (dotnet run)
npm run test:android-jvm -w packages/lynx        # Android core (OkHttp socket, files) on the JVM — JDK 17+ and Gradle 8.14
npm run check:android-module -w packages/lynx    # the module against the Lynx 4.1.0 classes + Autolink provider check
npm run test:ios-core -w packages/lynx           # iOS file core under GNUstep (Linux, or WSL on Windows)
npm run pack:all                                 # both tarballs into js/artifacts/npm
```

The Release build is warning-clean across all nine projects. CI runs through `azure-pipelines.yml` on `main`. It builds, runs the .NET tests, verifies the TypeScript and Lynx packages (including the interop run against the sample server and the Android checks), and only then publishes the NuGet and npm packages.

### Releases

A release is a version bump: set `<G9PackageVersion>` in `G9SignalRSuperNetCore/Directory.Build.props` (the one coordinate all four packages and the embedded generator are built from), add the entry under [What's new](#whats-new), and push to `main`. Under the parity rule the npm packages carry the same version: bump `version` in `js/packages/*/package.json`, the `@g9tm/signalr-supernetcore-client` pins in `js/packages/lynx/package.json`, the exported `VERSION` in both `src/index.ts`, the native module versions (Android `G9SignalRLynxModule.VERSION`, iOS `G9SignalRLynxVersion` and the podspec, the Lynxtron bridge) and `js/packages/client/test/misc.test.ts`. `npm run check:versions` lists every place that disagrees. The pipeline does the rest, in this order:

1. build, then the .NET tests, then the TypeScript/Lynx workspace (`npm ci`, `npm run verify`, the interop run, the Android JVM and module checks, `npm run pack:all`);
2. push the four packages to nuget.org (`--skip-duplicate`, so re-running a version that is already there is harmless);
3. publish the two npm packages (only when the `NpmToken` secret exists; a version already on npm is skipped);
4. **tag the released commit** `v<version>` (2.8+), an annotated tag, e.g. `v2.10.0`;
5. sync the repository, tags included, to the GitHub mirror.

**npm setup (one time).** Create the npm organization `g9tm` (free for public packages), an *automation* access token with publish rights to it, and a secret pipeline variable `NpmToken` holding it. Without the variable the publish step logs that it skipped and the tarballs are still attached to the run as the `npm` artifact.

The tag step runs only on `main` and only when everything before it succeeded, so a tag always names a commit whose packages are on NuGet. It reads the version from `Directory.Build.props` and checks that each package glob holds a `.nupkg` of exactly that version before tagging. A tag that already exists is left where it is — tags are never moved. A tagging problem is reported as a warning and the run ends as *succeeded with issues*: by then the packages are public, and a red release would say the opposite.

**One-time setup — the build service identity needs the "Create tag" permission.** The tag is pushed with the pipeline's own token (`System.AccessToken`), not with a personal access token. In Azure DevOps: *Project settings → Repositories → G9SignalRSuperNetCore → Security*, select **`<project name> Build Service (<organization>)`** (or *Project Collection Build Service* if the pipeline runs with collection scope) and set **Create tag** to **Allow**. Without it the push is refused (`TF401027 … needs 'CreateTag'`), the step logs a warning that says so, and the release itself is unaffected. The mirror needs nothing new: the tags travel with the sync step's existing GitHub credentials.

Releases before 2.8.0 were not tagged. To tag one by hand: `git tag -a v2.7.0 <commit> -m "Release 2.7.0"` and `git push origin v2.7.0`.

## Project layout

```
(repository root)
├── README.md                                               # this file (also packed into every NuGet package)
├── LICENSE.md                                              # MIT
├── .gitignore                                              # excludes uploads/, partials, build.log
├── AGENTS.md, CLAUDE.md                                    # how agents (and people) keep the .NET and Lynx/TS sides in step
├── azure-pipelines.yml                                     # build, tests, TS/Lynx verify + interop, NuGet + npm push, tag, mirror
├── js/                                                     # npm workspace (version = G9PackageVersion)
│   ├── packages/client/                                    # @g9tm/signalr-supernetcore-client (TypeScript twin, browser/Node/Lynx)
│   │   ├── src/                                            # G9Client, authorize, uploader/downloader, quality monitor, byte sources,
│   │   │                                                   # file systems (src/node: nodeFileSystem), base64/UTF-8 without web APIs
│   │   ├── test/                                           # vitest, incl. parity.test.ts against the .NET sources; test/lynxsim
│   │   └── README.md, PARITY.md, LICENSE.md
│   ├── packages/lynx/                                      # @g9tm/signalr-supernetcore-lynx (Lynx platform layer)
│   │   ├── src/                                            # createLynxClient, G9LynxWebSocket, lynxFileSystem, URL shim, src/lynxtron (desktop bridge)
│   │   ├── types/ generated/ lynx.lib.json                 # the native module contract (codegen input/output) + Autolink
│   │   ├── android/ jvm/ android-check/                    # Kotlin module (OkHttp), JVM tests, compile check vs Lynx 4.1.0
│   │   ├── ios/                                            # Objective-C module (NSURLSession) + podspec; ios/Tests/linux (GNUstep)
│   │   ├── test/                                           # unit, test/lynxsim (Lynx-shaped sandbox), test/interop (real .NET server)
│   │   └── README.md, Native-Validation-Pending.md
│   └── scripts/                                            # lynx-sandbox.mjs, check-versions.mjs, check-lynx-runtime.mjs, pack-all.mjs
└── G9SignalRSuperNetCore/                                  # the .NET solution (below)

G9SignalRSuperNetCore/
├── Directory.Build.props                                   # the one version coordinate + package metadata
├── G9SignalRSuperNetCore.sln
├── G9SignalRSuperNetCore.Server/                           # NuGet: server library + embedded generator
│   ├── G9SignalRSuperNetCoreServer.cs                      # Add… extension methods (DI helpers)
│   ├── Classes/Abstracts/                                  # Hub bases (4 variants) + G9ASession
│   ├── Classes/Attributes/                                 # DenyAccess, Exclude, RateLimit (+ G9ERateLimitScope),
│   │                                                       # ConnectionLimit, ConnectionRequired,
│   │                                                       # RequireRole, RequireClaim, RequirePermission, Telemetry
│   ├── Classes/Authorization/                              # IG9HubPermissionHandler (2.9)
│   ├── Classes/Connections/                                # IG9UserConnectionIndex + G9CUserConnectionIndex (2.9)
│   ├── Classes/Errors/G9CErrorCodes.cs                     # G9_RATE_LIMITED, G9_CONNECTION_LIMIT, …
│   ├── Classes/Filters/                                    # G9CHubFilter + G9CTokenBucket +
│   │                                                       # G9CConnectionCounter + G9CKeyRefCounter (2.9) + G9CTelemetry
│   ├── Classes/FileUpload/                                 # IG9UploadService + G9CUploadService +
│   │                                                       # G9CUploadCleanupService + DTOs (G9Dt…) and options
│   ├── Classes/Helper/                                     # G9JWTokenFactory + deny policy
│   ├── Classes/Hubs/                                       # G9GetJwtHub + G9CJwtRouteRegistry +
│   │                                                       # G9CAuthThrottle + G9DtJwtAuthOptions (2.9)
│   ├── Classes/Sessions/                                   # IG9SessionStore + in-memory impl
│   └── Enums/G9ESecurityAlgorithms.cs
├── G9SignalRSuperNetCore.Client/                           # NuGet: client library
│   ├── G9SignalRSuperNetCoreClient.cs                      # AOT-safe base (no Castle)
│   ├── G9SignalRSuperNetCoreClientWithJWTAuth.cs           # adds AuthorizeAsync flow
│   ├── G9DtClientConnectionOptions.cs                      # opt-in: stateful reconnect, WebSockets first (2.8)
│   ├── G9CClientReconnectPolicy.cs                         # jittered backoff for automatic reconnect
│   ├── G9CConnectionQualityMonitor.cs                      # G9Ping-based RTT / quality levels (2.9)
│   ├── G9HubConnectionExtensions.cs                        # WaitUntilConnectedAsync (2.9)
│   └── FileUpload/                                         # G9CFileUploader + DTOs + options
├── G9SignalRSuperNetCore.SourceGenerator/                  # Roslyn IIncrementalGenerator
│   ├── G9HubClientGenerator.cs                             # generator entry point
│   ├── G9Parser.cs                                         # symbol -> HubModel
│   ├── G9Emitter.cs                                        # HubModel -> C# source
│   ├── G9ClientTwins.cs                                    # server file-transfer DTOs -> client twins
│   ├── G9Diagnostics.cs                                    # G9001..G9007 stable IDs
│   └── AnalyzerReleases.{Shipped,Unshipped}.md             # release-tracking metadata
├── G9SignalRSuperNetCore.Server.MessagePack/               # NuGet: opt-in MessagePack protocol (server) + library shapes
├── G9SignalRSuperNetCore.Client.MessagePack/               # NuGet: opt-in MessagePack protocol (client) + library shapes
├── G9SignalRSuperNetCore.Tests/                            # xUnit: Kestrel + typed client, both protocols, registration
├── G9SignalRSuperNetCore.Sample.Shared/                    # Sample hub + client interface + ChatWireShapes
├── G9SignalRSuperNetCore.WebServer/                        # Sample server
└── G9SignalRSuperNetCore.ConsoleClient/                    # Sample client (Consolonia TUI)
```

---

## Migration guide

### 2.9 → 2.10

The .NET packages need nothing: their code is the same as 2.9.0. For TypeScript consumers:

1. **Rename the import.** `@g9/signalr-supernetcore-client` is now `@g9tm/signalr-supernetcore-client` (same API). A
   G9Hub-style vendored tarball is now `g9tm-signalr-supernetcore-client-2.10.0.tgz`.
2. **Additive API:** `G9ByteSource` / `bytesSource` / `toByteSource`, `G9FileSystem` / `fileByteSource`,
   `G9FileUploader.uploadFile`, `G9FileDownloader.downloadBytes` / `downloadToFile`, `G9ClientOptions.webSocket` /
   `eventSource` / `skipNegotiation` / `httpClient` / `httpTimeoutMs`, the `./node` entry (`nodeFileSystem`), and
   `base64ToBytes` / `bytesToBase64` / `utf8Encode` / `utf8Decode`. `upload()` still takes a Blob or File.
3. **Lynx apps** add `@g9tm/signalr-supernetcore-lynx` and build clients with `createLynxClient` (see [Lynx](#lynx)).

### 2.8 → 2.9

2.9 is **additive**: every existing call compiles and binds to the overload it bound to before. One new default changes behaviour (item 1). Items 3, 5–7 and 10 are fixes whose effect you may notice.

1. **The JWT authorize route is throttled, and the throttle is ON by default.** Once `AddSignalRSuperNetCoreJwt` is called (it registers the throttle), every `Authorize` call on a route mapped with `AddSignalRSuperNetCoreJwtHub` is charged to the caller's IP address: 60 calls a minute, with a burst of 20. Over that, the client gets `IsAccepted = false` with `RejectionReason = "G9_RATE_LIMITED"`, and your `authenticate` delegate is not called. Normal sign-ins never come near the limit, but integration tests or load tests that sign in many times from one machine may. Change it through the new `configureAuth` argument:

   ```csharp
   // raise it
   builder.Services.AddSignalRSuperNetCoreJwt(hubPath, parameters, configureAuth: o =>
   {
       o.AuthorizePerMinutePerIp = 600;
       o.AuthorizeBurstPerIp     = 100;
   });

   // or turn it off
   builder.Services.AddSignalRSuperNetCoreJwt(hubPath, parameters, configureAuth: o => o.ThrottleEnabled = false);
   ```

   Behind a reverse proxy, add `app.UseForwardedHeaders(...)` so the throttle sees the real client address. Otherwise all clients share the proxy's allowance.
2. **`G9Ping` is a new hub method on every G9 hub.** If one of your hubs already declares a method called `G9Ping`, rename it: SignalR does not allow overloaded hub methods, and the compiler warns about hiding. `G9Ping` is not added to generated typed clients. It is rate-limited per connection, and a class-level `[G9AttrRequirePermission]` applies to it like to any other method of the hub.
3. **Uploads change nothing unless you opt in, with two exceptions.** `PerUserNamespace` and `RandomizeCommittedNames` are off by default, and with the namespace off the partial layout is unchanged, so uploads in flight at upgrade time resume as before. The two exceptions: results now carry the new `StoredFileName`, and the hosted cleanup now deletes partials older than `PartialTtl` (24 h) every 10 minutes. If you relied on abandoned partials being kept, raise `PartialTtl` or set `CleanupInterval = null`. Turning `PerUserNamespace` on stores new partials under owner-scoped ids; partials begun earlier under the plain id are not found through the owner-aware overloads, and they expire through the cleanup.
4. **Custom `IG9UploadService` implementations keep compiling.** The owner-aware overloads have default implementations that call your existing methods and ignore the owner. Override them to honour ownership.
5. **Rate limits below about 0.5 calls a second are now enforced as written.** Before 2.9 they refilled at roughly one call a second. If a slow limit now bites where it did not before, the attribute always said that; raise the value.
6. **`[G9AttrConnectionLimit]` counts per hub, and slots no longer leak.** Before 2.9 the per-user and per-IP counters were shared by all hubs of the process, and a connection dropped abruptly never gave its slot back. Now each hub has its own counters and every slot is returned. A user with open connections to two limited hubs therefore has two separate allowances.
7. **`G9CFileUploader` waits for the connection.** When the connection is not connected at the start of an attempt, `UploadAsync` now waits up to `ReconnectGrace` (2 minutes by default) for the automatic reconnect and then throws `TimeoutException`; before, the `BeginUpload` call failed at once. With `ReconnectGrace = TimeSpan.Zero` it fails at once again, with that `TimeoutException`.
8. **New codes and log events.** `G9_PERMISSION_REQUIRED` and `G9_UPLOAD_FORBIDDEN`; log event ids 9102 (auth throttle, category `G9GetJwtHub`) and 9103 (a permission is required but no `IG9HubPermissionHandler` is registered). `G9CHubFilter.TrackedRateLimitConnections` now also counts shared user and IP buckets.
9. **The TypeScript twin moves in step.** Changes to the public surface of the .NET client now go together with `@g9/signalr-supernetcore-client` (2.9.0); see [TypeScript client (parity rule)](#typescript-client-parity-rule). Front ends on `@microsoft/signalr` can switch to it for the same reconnect, JWT, file-transfer and quality behaviour.
10. **Role, claim and telemetry attributes on a hub class now apply.** Before 2.9 the filter read them from the method only. If a hub class carries `[G9AttrRequireRole]` or `[G9AttrRequireClaim]`, every method of that hub (and `G9Ping`) now requires it — which is what the attribute always said; check that callers carry the role or claim. A method's role set no longer replaces the class set: both must pass.

### 2.7 → 2.8

2.8 is **additive**; nothing changes until you opt in.

1. **Nothing to change** for existing clients and servers. The new `ConfigureConnectionOptions` hook turns nothing on by default, and the new mapping overloads take an extra `bool`, so every existing call binds to the overload it bound to before.
2. **`Microsoft.Extensions.Http.Resilience`** moves from 9.10.0 to 10.0.0 in the net10.0 client. If your app pins a 9.x version of it (or of a `Microsoft.Extensions.Resilience` / `Http.Diagnostics` package), NuGet reports a downgrade (NU1605): move the pin to 10.x. An app that already references 10.x is unaffected.
3. **A new `G9EConnectionPhase.TransportFallback`** value exists (added last; the existing values keep their numbers). It is only ever reported by a client that turned `WebSocketsFirst` on. A `switch` over the enum with no default branch may get a compiler hint about the unhandled value.
4. **A client class that starts its `HubConnection` itself** (instead of calling the base `ConnectAsync`) and wants `WebSocketsFirst` should call the new `StartConnectionAsync(cancellationToken)` instead of `Connection.StartAsync(...)`; that is where the fallback lives. The library's own JWT client already does.
5. **Tagged releases** need one permission for the build service identity; see [Releases](#releases).

### 2.5 → 2.6

2.6 is additive apart from one source change in generated clients.

1. **Generated file-transfer types.** If you override a generated listener, or use a generated proxy method, whose signature names one of `G9SignalRSuperNetCore.Server.Classes.FileUpload.{G9DtBeginUploadResult, G9DtUploadResult, G9DtBeginDownloadResult, G9DtUploadProgress, G9EUploadStatus}`, change it to the same name under `G9SignalRSuperNetCore.Client.FileUpload`. The typical case is `UploadProgress`:

   ```csharp
   // before
   public override Task UploadProgress(G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadProgress progress) { … }
   // after
   public override Task UploadProgress(G9SignalRSuperNetCore.Client.FileUpload.G9DtUploadProgress progress) { … }
   ```

   The wire format is unchanged, so 2.5 servers and 2.6 clients interoperate. After the change, `G9CFileUploader` reports server-acknowledged bytes again.
2. **Route constants.** A hub declared in the same project as its typed client with `RoutePattern() => Route` now connects to `Route`; before, it connected to `/<ClassName>`. If you worked around that by mapping the hub at `/<ClassName>`, map it at `Route` instead.
3. **Service registration** now happens once per service collection. Nothing to change. Hosts built after the first one in a process now get the hub filter and JWT authentication.
4. **MessagePack** is opt-in; see [MessagePack hub protocol (opt-in)](#messagepack-hub-protocol-opt-in).

### 2.4 → 2.5

2.5 is **additive and zero-config**. Existing code keeps working unchanged. `G9CHubFilter`
gains a constructor that takes `ILogger<G9CHubFilter>`; DI supplies it automatically through
`AddSignalRSuperNetCoreCore()`, and a parameterless fallback (routing to `NullLogger`) keeps
`new G9CHubFilter()` working in tests. If you want the new policy-rejection warnings silenced,
raise the minimum level for the `G9SignalRSuperNetCore.Server.Classes.Filters.G9CHubFilter`
log category.

### 2.0 → 2.1

2.1 is **additive**. Existing 2.0 code keeps working without changes. To opt into the new features:

1. **Policy attributes**: just decorate hub methods. The hub filter is registered by `AddSignalRSuperNetCoreCore()` — no extra wiring.
2. **File upload**: register the service once during startup and add the two upload methods to your hub.

   ```csharp
   builder.Services.AddG9SignalRSuperNetCoreFileUpload(opt =>
   {
       opt.RootDirectory = Path.Combine(env.ContentRootPath, "uploads");
       opt.MaxBytes      = 5L * 1024 * 1024 * 1024;
   });
   ```

   Add `BeginUpload` and `UploadChunks` to your hub (snippets in [Resumable file upload](#resumable-file-upload)) and `Task UploadProgress(G9DtUploadProgress progress)` to your client interface.

3. **Telemetry / metrics**: add OpenTelemetry's `AddSource("G9SignalRSuperNetCore")` and `AddMeter("G9SignalRSuperNetCore")` to your tracing/metrics pipelines.

### 1.x → 2.0

The 2.0 release was a hardening release. Most consumer code keeps working with small registration changes:

1. **Service registration.** The old single-call helper

   ```csharp
   builder.Services.AddSignalRSuperNetCoreServerService<MyHub, IMyClient>();
   ```

   becomes the explicit composable API:

   ```csharp
   builder.Services.AddSignalRSuperNetCoreCore();
   builder.Services.AddG9SignalRSuperNetCoreSessionStore<MySession>();   // only if hub uses sessions
   builder.Services.AddSignalRSuperNetCoreJwt(MyHub.HubRoute, MyHub.TokenValidationParameters); // only for JWT hubs
   ```

2. **Hub mapping.** The old `app.AddSignalRSuperNetCoreServerHub<MyHub, IMyClient>()` (which read the route from a synthesized hub instance) becomes:

   ```csharp
   app.AddSignalRSuperNetCoreServerHub<MyHub, IMyClient>(routePattern: "/myhub");
   // …or, for JWT-authenticated hubs:
   app.AddSignalRSuperNetCoreJwtHub<MyHub, IMyClient>(
       hubRoutePattern:  MyHub.HubRoute,
       authRoutePattern: MyHub.AuthRoute,
       authenticate:     MyHub.AuthenticateAsync);
   ```

   Reading the route pattern from a synthesized instance required `Reflection.Emit`-style instance creation, which is incompatible with NativeAOT and full trim. Passing routes explicitly removes that dependency.

3. **Session lifecycle.** The static per-process dictionary on `G9AHubBaseWithSession*` is gone. Behavior is the same; the store comes from DI now.

4. **Client proxy.** The runtime Castle DynamicProxy proxy is replaced by source-generator output. Rebuild and the typed client appears in `obj/Generated/`. Override the listener methods on the generated class to receive callbacks.

5. **Removed client helpers.** `AssignListenerEvent`, `ListenOnceAsync`, and `SendThenListenOnceAsync` from 1.x are removed (heavy in reflection, not AOT-safe).

The Castle.Core dependency is no longer present. Remove any direct references you had to it from your client project.

## Roadmap

The 2.x line is shipped as a sequence of focused milestones.

- **2.0 — Foundation (shipped Sept 2026).** AOT/MAUI safety, source-generator typed client, pluggable session store, lock-free session counters, encapsulated JWT registry, warning-clean Release build.
- **2.1 — Policy & file upload (shipped).**
  - `[G9AttrRateLimit]`, `[G9AttrConnectionLimit]`, `[G9AttrConnectionRequired]`, `[G9AttrRequireRole]`, `[G9AttrRequireClaim]`, `[G9AttrTelemetry]`.
  - Stable error codes (`G9_*`) and `System.Diagnostics.Metrics` counters.
  - Resumable, hash-verified file upload with progress and safe resume on disconnect.
  - Generator: `IAsyncEnumerable<T>` parameters, `ValueTask`/`ValueTask<T>` returns, `CancellationToken` threading.
  - Tabbed Consolonia console test harness exercising every feature end-to-end.
- **2.2 — Groups & presence (shipped).**
  - `G9CGroupManager<THub>` — process-local group index with snapshot queries.
  - `G9CPresenceTracker` — per-user connection counts with `Channel<G9DtPresenceEvent>`.
  - `[G9AttrPresenceTracked]` — opt-in lifecycle tracking on the central filter.
  - `[G9AttrAutoJoinGroup]` — declarative group membership via a hub-typed AOT-safe filter.
  - Sample `ChatHub` ships `JoinRoom`, `LeaveRoom`, `SendToRoom`, `ListRoomMembers`, `ListOnlineUsers`.
- **2.3 — Streaming & resilience (shipped).**
  - `G9CResilientStream` — bounded producer/consumer pair with deterministic completion.
  - `G9DtStreamOptions` — capacity + drop policy (Wait / DropNewest / DropOldest).
  - `[G9AttrStreamBackpressure]` — declarative backpressure metadata.
  - `G9CClientReconnectPolicy` — drop-in replacement for `WithAutomaticReconnect()` with jittered exponential backoff and a max-elapsed budget.
- **2.4 — Distributed & secure (shipped, partial).**
  - `G9CHandshake` — ECDH P-256 + HKDF-SHA-256 + ChaCha20-Poly1305, BCL-only.
  - `G9CSessionSealer` — per-connection session-key cache, zeroed on disconnect.
  - `[G9AttrEncrypted]` — declarative encrypted-method contract.
  - `IG9DistributedBackplane` — pluggable cross-node coordination (default no-op for single-process; Redis/NATS implementations belong in optional packages).
  - Resumable downloads with SHA-256 verification and idempotent fast path.
  - Still planned for a future minor: `G9SignalRSuperNetCore.Server.Redis` package, BenchmarkDotNet suite.
- **2.5 — Server policy-rejection logging (shipped).**
  - `G9CHubFilter` emits structured `Warning` logs (event ids 9100 / 9101) on every policy rejection so operators can see *why* a call/connect was refused; DI-injected `ILogger<G9CHubFilter>` with a `NullLogger` fallback. Metrics unchanged.

- **2.6 — MessagePack & fixes (shipped).**
  - `G9SignalRSuperNetCore.Server.MessagePack` / `.Client.MessagePack` — opt-in, AOT-safe binary hub protocol with the library's own type shapes built in.
  - Generated clients use the client-library file-transfer twins (server acknowledgements reach `G9CFileUploader` again); same-project hubs resolve `Route` constants.
  - Registration idempotent per service collection; test project run by CI before publishing.
- **2.7 — Call semantics & review fixes (shipped).** See [2.7](#27--awaited-calls-really-wait-and-four-correctness-fixes-from-an-external-review).
- **2.8 — Connection options (shipped).**
  - `G9DtClientConnectionOptions` through the `ConfigureConnectionOptions` hook: stateful reconnect (with `allowStatefulReconnects` on the server mapping helpers and `AddG9SignalRSuperNetCoreStatefulReconnect`), and WebSockets first with a fallback to negotiation that keeps the `HubConnection`.
  - Thread-safe jitter in `G9CClientReconnectPolicy`; `Microsoft.Extensions.Http.Resilience` on the 10.x line; releases tagged `v<version>` by the pipeline.
- **2.9 — Ownership and abuse controls (shipped).** See [2.9](#29--scoped-rate-limits-auth-route-throttle-upload-ownership-permissions-connection-index-and-quality).
  - Rate-limit scopes (`Connection` / `User` / `Ip`) with shared buckets freed by the last connection; slow rates enforced as written.
  - Per-IP throttle on the JWT authorize route, on by default (`G9DtJwtAuthOptions`).
  - Upload ownership: `PerUserNamespace`, `RandomizeCommittedNames`, `StoredFileName`, the `Authorize` hook, owner-aware service overloads, and a hosted cleanup that removes `.bin` and `.meta` together.
  - `[G9AttrRequirePermission]` + `IG9HubPermissionHandler`; `IG9UserConnectionIndex` (online users, abort a user); `G9Ping` + `G9CConnectionQualityMonitor`; `WaitUntilConnectedAsync`; telemetry `SampleRate`.
  - IL2070 gone: streaming is detected from the declared return type. The TypeScript client (`js/`, `@g9/signalr-supernetcore-client`) is kept in parity with the .NET client.
- **2.10 — Lynx (this release, 2.10.0).** See [2.10](#210--lynx-the-typescript-client-on-lynx-a-native-websocket-and-files-npm-under-g9tm).
  - npm under `@g9tm`: `@g9tm/signalr-supernetcore-client` (renamed from `@g9/…`) and `@g9tm/signalr-supernetcore-lynx` — a native binary WebSocket and file operations for Android, iOS and Lynxtron, proven on a real Lynx runtime (Android emulator).
  - Lynxtron: a refused connect ends the socket on Node 22 too (its WebSocket reports `error` and never `close`).
  - The .NET packages are unchanged apart from the version.
- **Next (planned).** Cluster-wide rate limits and connection index through `IG9DistributedBackplane` (Redis package); shared-storage upload partials for cross-node resume; BenchmarkDotNet suite.

The order can shift in response to consumer feedback; track progress in the issues board.

## Contributing

Issues and pull requests are welcome.

1. Open an issue describing the change before sending a large PR.
2. Match the existing code style: file-scoped namespaces, XML docs on public members, `G9` prefix on public types, `G9C…` for concrete classes, `G9A…` for abstracts, `G9Dt…` for DTOs, `G9Attr…` for attributes, `G9E…` for enums.
3. `dotnet build -c Release` must finish with zero warnings — including AOT and trim warnings attributable to G9 code — and `dotnet test` must pass.
4. Update or add a sample under `G9SignalRSuperNetCore.WebServer` / `G9SignalRSuperNetCore.ConsoleClient` when adding new public surface.
5. A change to the public surface of the .NET client (or to the hub contract it uses) lands in `js/` in the same change, with [PARITY.md](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/js/packages/client/PARITY.md) updated, and `npm run verify` in `js/` must pass. A change to the native module contract lands on Android, iOS and the Lynxtron bridge together. See [the parity rule](#typescript-client-parity-rule) and [AGENTS.md](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/AGENTS.md).
6. New cryptographic code (in 2.4+) must use BCL primitives only and ship with property-based tests.

## License

Released under the [MIT License](https://github.com/ImanKari/G9SignalRSuperNetCore/blob/main/LICENSE.md). Copyright (c) 2024-present Iman Kari (G9TM).

The same license text ships as `LICENSE.md` in every NuGet package and in the npm packages `@g9tm/signalr-supernetcore-client` and `@g9tm/signalr-supernetcore-lynx`.
