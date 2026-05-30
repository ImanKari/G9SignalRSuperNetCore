# G9SignalRSuperNetCore

[![NuGet — Server](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.svg?style=flat-square&label=Server)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server/)
[![NuGet — Client](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Client.svg?style=flat-square&label=Client)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Client/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![AOT-safe](https://img.shields.io/badge/NativeAOT-ready-success?style=flat-square)](#maui-and-nativeaot-support)
[![MAUI](https://img.shields.io/badge/MAUI-Android%20%7C%20iOS%20%7C%20Mac%20%7C%20Win-7160E8?style=flat-square)](#maui-and-nativeaot-support)
[![Generator](https://img.shields.io/badge/SourceGen-Incremental-3E8FFF?style=flat-square)](#auto-generated-typed-client)

**G9SignalRSuperNetCore** is a strongly-typed, AOT-friendly, scale-aware wrapper around ASP.NET Core SignalR that lets you build real-time hubs and clients with less ceremony and more safety.

It bundles the things most SignalR projects end up reinventing — typed proxies, JWT authentication, per-user session state, declarative policy attributes (rate limit, connection limit, role/claim guards, telemetry), resumable file upload with progress and SHA-256 verification — and ships them as opt-in, AOT-safe, allocation-conscious primitives.

> Drop reflection-based runtime proxies, get a build-time-generated typed client. Drop static per-process state, get a pluggable session store. Add `[G9AttrRateLimit]` to a method and you're rate-limited; add `[G9AttrTelemetry]` and you have OpenTelemetry traces. Keep the SignalR programming model you already know.

---

## Table of contents

- [What's new](#whats-new)
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
- [Policy attributes](#policy-attributes)
- [Resumable file upload](#resumable-file-upload)
- [Resumable file download](#resumable-file-download)
- [Groups and presence](#groups-and-presence)
- [Server streams with backpressure](#server-streams-with-backpressure)
- [Resilient client reconnect](#resilient-client-reconnect)
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

- `G9CResilientStream.Create<T>(options)` — bounded producer/consumer pair that completes the channel deterministically (avoids the canonical SignalR streaming footgun where a thrown exception leaves the channel hanging open).
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

- `[G9AttrRateLimit(perSecond: 5, burst: 10)]` — per-(connection, method) lock-free token bucket. Rejects with `G9_RATE_LIMITED` and emits a metric.
- `[G9AttrConnectionLimit(perUser: 5, perIp: 50)]` — class-level cap on simultaneous connections. Rejects with `G9_CONNECTION_LIMIT` before `OnConnectedAsync` returns.
- `[G9AttrConnectionRequired]` — fail-fast guard for methods that should not run on aborted connections.
- `[G9AttrRequireRole("admin")]` / `[G9AttrRequireRole("a","b")]` — shorthand for role-gated methods, returning `G9_ROLE_REQUIRED`.
- `[G9AttrRequireClaim("scope", "chat:write")]` — claim-gated methods, returning `G9_CLAIM_REQUIRED`.
- `[G9AttrTelemetry]` / `[G9AttrTelemetry("name")]` — emits `ActivitySource` spans (`G9SignalRSuperNetCore`) with connection id, user id, and outcome.

**Stable error codes** (`G9CErrorCodes`): `G9_RATE_LIMITED`, `G9_CONNECTION_LIMIT`, `G9_CONNECTION_REQUIRED`, `G9_ROLE_REQUIRED`, `G9_CLAIM_REQUIRED`, `G9_UPLOAD_TOO_LARGE`, `G9_UPLOAD_HASH_MISMATCH`, `G9_UPLOAD_UNKNOWN_ID`, `G9_UPLOAD_FAILED`. Clients switch on `HubException.Message` to render localized errors or trigger recovery.

**Built-in metrics** through `System.Diagnostics.Metrics` on the `G9SignalRSuperNetCore` meter: `g9.signalr.rate_limited_invocations`, `g9.signalr.connection_limit_rejections`, `g9.signalr.authorization_rejections`. Subscribe with OpenTelemetry's `AddMeter("G9SignalRSuperNetCore")`.

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
- **Declarative policy attributes** — rate limiting, connection caps, role/claim guards, telemetry. Opt-in, lock-free, zero cost when unused.
- **Resumable file upload** — chunk-streamed, SHA-256-verified, append-only, with live progress and safe resume on disconnect.
- **Scale-out friendly** — no static per-process state on the correctness path.
- **MAUI and NativeAOT friendly** — every hot path free of `Reflection.Emit` and runtime proxy generation.

## Packages

| Package | Purpose |
|---|---|
| `G9SignalRSuperNetCore.Server` | Hub bases, JWT pipeline, session abstraction, attributes, hub filter, file-upload service, embedded source generator |
| `G9SignalRSuperNetCore.Client` | Typed client bases (anonymous + JWT), resumable file uploader, progress DTOs |

The source generator ships **inside the Server package** under `analyzers/dotnet/cs`. Consumers don't need a separate code-gen package; just reference `G9SignalRSuperNetCore.Server` (server) and `G9SignalRSuperNetCore.Client` (client) and the typed client lights up automatically.

All packages target **.NET 10.0** and are AOT-compatible and trim-safe.

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
│   │   • G9CTokenBucket per (conn,method) │   [G9AttrRateLimit]                │
│   │   • G9CConnectionCounter per user/IP │   [G9AttrConnectionLimit]          │
│   │   • role/claim guards                │   [G9AttrRequireRole / Claim]      │
│   │   • ActivitySource spans             │   [G9AttrTelemetry]                │
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
│   AddSignalRSuperNetCoreJwt(hubPath, validationParameters)                    │
│   AddSignalRSuperNetCoreJwtHub<THub, TClient>(hubRoute, authRoute, …)         │
│                                                                               │
│   /AuthHub  ─►  /SecureHub  (Authorize)                                       │
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
└──────────────────────────────────────────────────────────────────────────────┘
```

## Getting started

### Prerequisites

- .NET 10.0 SDK or later
- ASP.NET Core 10 project for the server
- Any .NET 10 project for the client (console, MAUI, WPF, Blazor, ASP.NET, etc.)

### Install

Server project:

```powershell
dotnet add package G9SignalRSuperNetCore.Server
```

Client project:

```powershell
dotnet add package G9SignalRSuperNetCore.Client
```

The source generator is shipped inside the Server package (`analyzers/dotnet/cs`). Both project-reference and NuGet-reference flows pick it up automatically.

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

For horizontal scale-out, replace the registration with a distributed implementation. A Redis-backed store is on the 2.2 roadmap and ships in a separate package so the core library has no Redis dependency.

```csharp
// 2.2 (preview):
builder.Services.AddG9SignalRSuperNetCoreRedisSessionStore<ChatSession>(
    redisConnectionString: builder.Configuration.GetConnectionString("Redis"));
```

## Auto-generated typed client

The library ships a Roslyn `IIncrementalGenerator` (`G9SignalRSuperNetCore.SourceGenerator`) that scans every project that consumes the library, finds your hubs, and emits a strongly-typed, AOT-safe client for each one. No reflection, no `MethodInfo.Invoke`, no Castle DynamicProxy.

### How to wire it up

The generator ships **embedded inside the `G9SignalRSuperNetCore.Server` package** under `analyzers/dotnet/cs`. Three project layouts are supported:

**Layout A — single project** (rare; you can't do this if your client is a different platform than your server):

```xml
<ItemGroup>
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.4.1" />
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.4.1" />
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
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.4.1" />
</ItemGroup>

<!-- MyApp.Server.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MyApp.Shared\MyApp.Shared.csproj" />
</ItemGroup>

<!-- MyApp.Client.csproj -->
<ItemGroup>
  <ProjectReference Include="..\MyApp.Shared\MyApp.Shared.csproj" />
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.4.1" />
</ItemGroup>
```

The generator runs in `MyApp.Shared` (because that's where the hub class lives) and the typed `ChatHubClient` is emitted into `MyApp.Shared`'s compilation. `MyApp.Client` references the Shared library and gets the typed client for free.

**Layout C — generator only on the client side** (when the client project doesn't reference the shared library):

```xml
<!-- MyApp.Client.csproj -->
<ItemGroup>
  <PackageReference Include="G9SignalRSuperNetCore.Server" Version="2.4.1"
                    PrivateAssets="all" />        <!-- only the analyzer is needed -->
  <PackageReference Include="G9SignalRSuperNetCore.Client" Version="2.4.1" />
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
- `ChatHubServerProxy` — internal sealed class that turns each method on `IChatHubMethods` into a single `HubConnection.SendCoreAsync` / `InvokeCoreAsync<T>` / `StreamAsyncCore<T>` call.

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
| `G9002` | Warning | Listener method returns something other than `Task` / `ValueTask`. |
| `G9003` | Warning | Hub method has more than 16 parameters (SignalR limit). |
| `G9004` | Warning | Generic hub methods are not supported. |
| `G9005` | Warning | Hub class is missing a `RoutePattern()` override. |
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

Each method on the server interface maps to a single typed call on the underlying `HubConnection`. `Task` methods use `SendCoreAsync`; `Task<T>` methods use `InvokeCoreAsync<T>`; `IAsyncEnumerable<T>` uses `StreamAsyncCore<T>`. No `MethodInfo.Invoke`, no boxing of return values.

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

Automatic reconnect (`WithAutomaticReconnect()`) is enabled by default and the server timeout is 60 seconds. Both can be customized through the `customConfigureBuilder` and `configureHttpConnection` constructor parameters of the generated client.

### Connection access

If you need to drop down to raw SignalR APIs, the underlying `HubConnection` is exposed via `client.Connection`:

```csharp
await foreach (var item in client.Connection.StreamAsync<int>("MyStream"))
    Console.WriteLine(item);
```

### `IAsyncDisposable`

The base client implements `IAsyncDisposable`. `await using var client = new ChatHubClient(url);` disposes the connection cleanly.

---

## Policy attributes

All attributes are opt-in. Apply them per-method (or per-class for connection limits) to enforce production-grade policies declaratively. The hub filter (`G9CHubFilter`, registered automatically by `AddSignalRSuperNetCoreCore()`) reads them from a per-method `MethodMeta` cache, so methods with **no** G9 attributes pay only the cost of one dictionary lookup per call.

| Attribute | Target | Rejected with | Notes |
|---|---|---|---|
| `[G9AttrRateLimit(perSecond, burst)]` | method | `G9_RATE_LIMITED` | Lock-free token bucket per `(connectionId, method)`. Bursts up to `burst`, refills at `perSecond`. |
| `[G9AttrConnectionLimit(perUser, perIp)]` | class | `G9_CONNECTION_LIMIT` | Caps simultaneous connections. `0` disables that dimension. Enforced on connect, before `OnConnectedAsync`. |
| `[G9AttrConnectionRequired]` | method | `G9_CONNECTION_REQUIRED` | Fails fast when `Context.ConnectionAborted` is already cancelled. |
| `[G9AttrRequireRole("admin")]` | method or class | `G9_ROLE_REQUIRED` | Caller must carry at least one of the listed roles. Stackable. |
| `[G9AttrRequireClaim("scope", "chat:write")]` | method or class | `G9_CLAIM_REQUIRED` | Caller must carry the claim. Empty `acceptedValues` accepts any value. Stackable. |
| `[G9AttrTelemetry]` / `[G9AttrTelemetry("name")]` | method or class | n/a | Wraps invocation in an `ActivitySource` span tagging connection id, user id, and outcome (`ok` / `cancelled` / `faulted`). |
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
        return await svc.BeginAsync(uploadId, fileName, totalBytes, chunkSize, declaredSha256Hex,
            Context.ConnectionAborted);
    }

    public async Task<G9DtUploadResult> UploadChunks(string uploadId, IAsyncEnumerable<byte[]> chunks)
    {
        var svc    = ResolveUploadService();
        var caller = Clients.Caller;

        return await svc.AppendChunksAsync(
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
- After commit: atomically renamed to `{RootDirectory}/{fileName}` (timestamp-suffixed if the name collides)

The repo's `.gitignore` excludes `**/uploads/` and `**/.partial/` so committed files and partials never accidentally reach the repo.

### Client side

```csharp
using G9SignalRSuperNetCore.Client.FileUpload;

var uploader = new G9CFileUploader(client.Connection, new G9DtUploadClientOptions
{
    ChunkSize  = 64 * 1024,           // 64 KB; capped at 4 MB by the server
    MaxRetries = 5,                   // exponential backoff between retries
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
        Console.WriteLine($"OK — {result.BytesWritten} B at {result.FinalPath}");
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

### Resume guarantees

- **Append-only writes** server-side, so a partial file is never corrupted by retry.
- **Idempotent**: replaying the same chunk after a crash never duplicates bytes (server compares offset against current file length and refuses to over-write).
- **Tamper-detection**: the client's declared SHA-256 is verified before commit. Mismatch deletes the partial and returns `G9_UPLOAD_HASH_MISMATCH`.
- **Per-id concurrency** through a `SemaphoreSlim` so two parallel callers for the same `uploadId` never interleave bytes.
- **Cancellation-clean**: `OperationCanceledException` preserves the partial so a future call can resume.

### Out of scope (today)

- **Cross-server resume in a load-balanced cluster** — the partial lives on whichever server node first received chunks. With sticky sessions you're fine; without them, a future call may land on a different node and start over. The Bundle 5 Redis package and a shared blob backend will close this.
- **Bandwidth throttling / pause-resume from the UI** — easy to add on top; not in 2.1.

## Resumable file download

Symmetric to the upload pipeline. The server exposes `BeginDownloadAsync(serverRelativePath, resumeFrom, chunkSize)` and a streaming `StreamFileAsync(...)`. The client uses `G9CFileDownloader` and gets the same resume guarantees.

```csharp
// Server hub method (sample ChatHub):
[G9AttrTelemetry]
public IAsyncEnumerable<byte[]> DownloadChunks(string fileName, long resumeFrom, int chunkSize, CancellationToken ct)
    => _uploads.StreamFileAsync(fileName, resumeFrom, chunkSize, ct);

// Client:
var downloader = new G9CFileDownloader(connection, new G9DtDownloadClientOptions
{
    ChunkSize = 64 * 1024,
    OnRetry = info => Console.WriteLine($"resume from {info.BytesAlreadyOnServer} after {info.Backoff}")
});
var result = await downloader.DownloadAsync(
    serverFileName: "file_upload_test.zip",
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
- `g9.signalr.authorization_rejections` — calls rejected by `[G9AttrRequireRole]` / `[G9AttrRequireClaim]`.

### Policy-rejection logging

In addition to the metrics above, `G9CHubFilter` emits a structured `Warning`-level log entry every time a policy refuses an invocation, so an operator reading the server log can see *why* a call or connect was rejected (not just that the client received a `HubException`). The filter resolves an `ILogger<G9CHubFilter>` from DI; when constructed outside DI (`new G9CHubFilter()`), logging routes to `NullLogger` and only the metrics fire. Two event ids:

- `9100` — per-invocation rejection (rate limit, role, claim, connection-required). Fields: `ErrorCode`, `Method`, `ConnectionId`, `UserId`, `Detail`.
- `9101` — per-connect rejection (connection limit). Fields: `Hub`, `Dimension` (`per-user`/`per-ip`), `Key`, `Limit`.

Filter these out by raising the minimum level for the `G9SignalRSuperNetCore.Server.Classes.Filters.G9CHubFilter` category if the warnings are noisy in your environment.

Stable error codes (`G9SignalRSuperNetCore.Server.Classes.Errors.G9CErrorCodes`):

```
G9_RATE_LIMITED          G9_CONNECTION_LIMIT       G9_CONNECTION_REQUIRED
G9_ROLE_REQUIRED         G9_CLAIM_REQUIRED
G9_UPLOAD_TOO_LARGE      G9_UPLOAD_HASH_MISMATCH   G9_UPLOAD_UNKNOWN_ID    G9_UPLOAD_FAILED
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
- **File-upload writes are serialized per `uploadId`** through a `SemaphoreSlim`. Two racing append calls for the same upload never interleave bytes; two calls for *different* uploads run in parallel.

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

The console app uses `Consolonia.UseAutoDetectedConsole()` so it works in Windows Terminal, ConEmu, iTerm2, GNOME Terminal, Alacritty, etc.

---

## Build and test

```powershell
# Restore + Release build the whole solution
dotnet build G9SignalRSuperNetCore/G9SignalRSuperNetCore.sln -c Release

# Run the sample web server
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.WebServer -c Release

# Run the Consolonia console test harness (in another terminal)
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.ConsoleClient -c Release
```

The Release build is warning-clean across all six projects. CI runs through `azure-pipelines.yml` and publishes the two NuGet packages on `main`.

## Project layout

```
G9SignalRSuperNetCore/
├── G9SignalRSuperNetCore.sln
├── .gitignore                                              # excludes uploads/, partials, build.log
├── G9SignalRSuperNetCore.Server/                           # NuGet: server library + embedded generator
│   ├── G9SignalRSuperNetCoreServer.cs                      # Add… extension methods (DI helpers)
│   ├── Classes/Abstracts/                                  # Hub bases (4 variants) + G9ASession
│   ├── Classes/Attributes/                                 # DenyAccess, Exclude, RateLimit,
│   │                                                       # ConnectionLimit, ConnectionRequired,
│   │                                                       # RequireRole, RequireClaim, Telemetry
│   ├── Classes/Errors/G9CErrorCodes.cs                     # G9_RATE_LIMITED, G9_CONNECTION_LIMIT, …
│   ├── Classes/Filters/                                    # G9CHubFilter + G9CTokenBucket +
│   │                                                       # G9CConnectionCounter + G9CTelemetry
│   ├── Classes/FileUpload/                                 # IG9UploadService + G9CUploadService +
│   │                                                       # DTOs (G9Dt…) and options
│   ├── Classes/Helper/                                     # G9JWTokenFactory + deny policy
│   ├── Classes/Hubs/                                       # G9GetJwtHub + G9CJwtRouteRegistry
│   ├── Classes/Sessions/                                   # IG9SessionStore + in-memory impl
│   └── Enums/G9ESecurityAlgorithms.cs
├── G9SignalRSuperNetCore.Client/                           # NuGet: client library
│   ├── G9SignalRSuperNetCoreClient.cs                      # AOT-safe base (no Castle)
│   ├── G9SignalRSuperNetCoreClientWithJWTAuth.cs           # adds AuthorizeAsync flow
│   └── FileUpload/                                         # G9CFileUploader + DTOs + options
├── G9SignalRSuperNetCore.SourceGenerator/                  # Roslyn IIncrementalGenerator
│   ├── G9HubClientGenerator.cs                             # generator entry point
│   ├── G9Parser.cs                                         # symbol -> HubModel
│   ├── G9Emitter.cs                                        # HubModel -> C# source
│   ├── G9Diagnostics.cs                                    # G9001..G9007 stable IDs
│   └── AnalyzerReleases.{Shipped,Unshipped}.md             # release-tracking metadata
├── G9SignalRSuperNetCore.Sample.Shared/                    # Sample hub + client interface
├── G9SignalRSuperNetCore.WebServer/                        # Sample server
└── G9SignalRSuperNetCore.ConsoleClient/                    # Sample client (Consolonia TUI)
```

---

## Migration guide

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
- **2.1 — Policy & file upload (this release).**
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
  - Still planned for a future minor: `G9SignalRSuperNetCore.Server.Redis` package, MessagePack hub-protocol opt-in package, BenchmarkDotNet suite.
- **2.5 — Server policy-rejection logging (shipped).**
  - `G9CHubFilter` emits structured `Warning` logs (event ids 9100 / 9101) on every policy rejection so operators can see *why* a call/connect was refused; DI-injected `ILogger<G9CHubFilter>` with a `NullLogger` fallback. Metrics unchanged.

The order can shift in response to consumer feedback; track progress in the issues board.

## Contributing

Issues and pull requests are welcome.

1. Open an issue describing the change before sending a large PR.
2. Match the existing code style: file-scoped namespaces, XML docs on public members, `G9` prefix on public types, `G9C…` for concrete classes, `G9A…` for abstracts, `G9Dt…` for DTOs, `G9Attr…` for attributes, `G9E…` for enums.
3. `dotnet build -c Release` must finish with zero warnings — including AOT and trim warnings attributable to G9 code.
4. Update or add a sample under `G9SignalRSuperNetCore.WebServer` / `G9SignalRSuperNetCore.ConsoleClient` when adding new public surface.
5. New cryptographic code (in 2.4+) must use BCL primitives only and ship with property-based tests.

## License

Released under the [MIT License](LICENSE.md).
