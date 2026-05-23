# G9SignalRSuperNetCore

[![NuGet — Server](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.svg?style=flat-square&label=Server)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server/)
[![NuGet — Client](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Client.svg?style=flat-square&label=Client)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Client/)
[![NuGet — Generator](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.ClientInterfaceGenerator.svg?style=flat-square&label=ClientGenerator)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server.ClientInterfaceGenerator/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![AOT-safe](https://img.shields.io/badge/NativeAOT-ready-success?style=flat-square)](#maui-and-nativeaot-support)
[![MAUI](https://img.shields.io/badge/MAUI-Android%20%7C%20iOS%20%7C%20Mac%20%7C%20Win-7160E8?style=flat-square)](#maui-and-nativeaot-support)

**G9SignalRSuperNetCore** is a strongly-typed, AOT-friendly, scale-aware wrapper around ASP.NET Core SignalR that lets you build real-time hubs and clients with less ceremony and more safety. Hubs derive from typed base classes, clients use a build-time source generator to get a fully-typed `Server` proxy, JWT authentication is wired by default, and per-user session storage is pluggable so you can scale horizontally without rewriting hubs.

> Drop reflection-based runtime proxies, get a build-time generated typed client. Drop static per-process state, get a pluggable session store. Keep the SignalR programming model you already know.

---

## Table of contents

- [What's new in 2.0](#whats-new-in-20)
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
- [Attributes](#attributes)
- [JWT helper](#jwt-helper)
- [Scaling to many connections](#scaling-to-many-connections)
- [Thread safety contract](#thread-safety-contract)
- [Build and test](#build-and-test)
- [Project layout](#project-layout)
- [Migration guide (1.x → 2.0)](#migration-guide-1x--20)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## What's new in 2.0

Version 2.0 is a hardening release focused on correctness at scale and platform reach.

- **MAUI and NativeAOT ready.** All library projects declare `IsAotCompatible=true` and `EnableTrimAnalyzer=true`. The runtime Castle DynamicProxy dependency is gone; the typed `Server` proxy is now produced by a build-time source generator with no `Reflection.Emit`. Publishes cleanly under `PublishAot=true` with no trim warnings attributable to G9 code.
- **No reflection on the client hot path.** Server-method invocations and listener registrations are emitted as concrete typed code. Per-call CPU and allocations are dominated by the underlying `HubConnection`, not by intermediate reflection.
- **Pluggable session storage.** A new `IG9SessionStore<TSession>` abstraction replaces the static per-process dictionary. The default `G9CInMemorySessionStore<TSession>` is registered through DI and matches the previous behavior; swap it for a distributed implementation to scale across processes.
- **Lock-free, race-free session counters.** `G9ASession.ConnectionCounts` and `LastActivityDateTime` are mutated through `Interlocked` operations only. Connect/disconnect storms from the same user no longer drop or under-count.
- **Encapsulated JWT route registry.** The leading-underscore static dictionary on `G9GetJwtHub` is replaced by an internal registry. `JwtSecurityTokenHandler` is now reused process-wide.
- **Cleaner scale-out path.** No correctness-critical state lives in the library's static fields. The same hub source code works behind a Redis backplane or Azure SignalR Service.
- **Warning-clean Release build.** Zero AOT, trim, or compiler warnings attributable to G9 code.

A coming **2.1** release adds an opt-in application-layer secure channel (`ECDH + AEAD` with public-key pinning), `[G9AttrRateLimit]` / `[G9AttrConnectionRequired]` attributes, and a Redis-backed session store package.

## Why this library

Plain ASP.NET Core SignalR is excellent, but most teams end up building the same plumbing: a typed proxy for server methods, a registration block for client callbacks, a JWT pipeline that plays nicely with WebSockets, a per-user session, and a code-gen step so client and server never drift. G9SignalRSuperNetCore covers all of that:

- **Strongly-typed server hubs** through a generic `Hub<TClientInterface>` base.
- **Strongly-typed client proxy generated at build time** — call `client.Server.MyMethod(...)` directly.
- **Listener wiring with no reflection** — generated typed `On<...>` registrations match your interface.
- **JWT authentication out of the box** — separate auth route exchanges credentials for a token, then the protected hub uses `[Authorize]`.
- **Per-user session abstraction** — thread-safe counters, last-activity tracking, cleanup helpers, swappable backend.
- **Scale-out friendly** — no static per-process state on the correctness path.
- **MAUI and NativeAOT friendly** — all hot paths free of `Reflection.Emit` and runtime proxy generation.

## Packages

| Package | Purpose |
|---|---|
| `G9SignalRSuperNetCore.Server` | Hub base classes, JWT pipeline, session abstraction, attributes, helpers |
| `G9SignalRSuperNetCore.Client` | Typed client base classes for both anonymous and JWT-authenticated hubs |
| `G9SignalRSuperNetCore.Server.ClientInterfaceGenerator` | Build-time generator that emits the typed client from your hubs |

All packages target **.NET 10.0** and are AOT-compatible and trim-safe.

---

## MAUI and NativeAOT support

The library is designed for iOS, Android, MacCatalyst, Windows, and NativeAOT publishes from day one.

- **No `Reflection.Emit`.** The Castle DynamicProxy runtime proxy is gone. The typed `Server` proxy is concrete code emitted by the source generator at build time.
- **No reflection on the client hot path.** Listener registrations are typed `Connection.On<...>(...)` calls in generated code.
- **Trim and AOT analyzers enabled.** Every library project sets `IsAotCompatible=true` and `EnableTrimAnalyzer=true`, and public APIs that capture interface generic parameters propagate `[DynamicallyAccessedMembers]` so consumers do not see opaque trim warnings.
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
┌─────────────────────────────────────────────────────────────────────┐
│                            SERVER                                    │
│                                                                      │
│   G9AHubBase<THub, TClient>                                          │
│   ├─ G9AHubBaseWithJWTAuth<THub, TClient>                            │
│   ├─ G9AHubBaseWithSession<THub, TClient, TSession>                  │
│   └─ G9AHubBaseWithSessionAndJWTAuth<THub, TClient, TSession>        │
│                                                                      │
│   IG9SessionStore<TSession>  (DI)                                    │
│   └─ G9CInMemorySessionStore<TSession>  (default)                    │
│                                                                      │
│   AddSignalRSuperNetCoreCore(...)                                    │
│   AddG9SignalRSuperNetCoreSessionStore<TSession>()                   │
│   AddSignalRSuperNetCoreJwt(hubPath, validationParameters)           │
│   AddSignalRSuperNetCoreJwtHub<THub, TClient>(hubRoute, authRoute,…) │
│                                                                      │
│   /AuthHub  ─►  /SecureHub  (Authorize)                              │
└─────────────────────────────────────────────────────────────────────┘
                                  ▲
                                  │ WebSocket / SSE / LongPolling
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                            CLIENT                                    │
│                                                                      │
│   G9SignalRSuperNetCoreClient<TSelf, TServerMethods, TListeners>     │
│   └─ G9SignalRSuperNetCoreClientWithJWTAuth<...>                     │
│                                                                      │
│   client.Server.MyMethod(args)  ── source-generator typed proxy      │
│   listener methods on derived class ── source-generator typed wiring │
│   AOT-safe — no Castle, no Reflection.Emit                           │
└─────────────────────────────────────────────────────────────────────┘
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
dotnet add package G9SignalRSuperNetCore.Server.ClientInterfaceGenerator
```

Client project:

```powershell
dotnet add package G9SignalRSuperNetCore.Client
```

The `ClientInterfaceGenerator` package is optional but recommended — it emits the typed client class from your hubs at build time. See [Auto-generated typed client](#auto-generated-typed-client).

---

## Quick sample (no auth)

A minimal hub, a client interface for server-to-client callbacks, and a console client.

**Server — `Program.cs`**

```csharp
using G9SignalRSuperNetCore.Server;

var builder = WebApplication.CreateBuilder(args);

// Core services: SignalR + deny-by-default policy + custom UserIdProvider
builder.Services.AddSignalRSuperNetCoreCore();

var app = builder.Build();

// Map your hub at its declared route pattern
app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>(
    routePattern: "/chat");

app.MapGet("/", () => "Chat server is running");
app.Run();
```

**Server — `IChatClient.cs`** (callbacks the server invokes on the client)

```csharp
public interface IChatClient
{
    Task ReceiveMessage(string user, string message);
    Task UserJoined(string user);
}
```

**Server — `ChatHub.cs`**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;

public class ChatHub : G9AHubBase<ChatHub, IChatClient>
{
    public override string RoutePattern() => "/chat";

    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);

    public override async Task OnConnectedAsync()
    {
        await Clients.Others.UserJoined(Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}
```

**Client — generated typed client (preferred)**

Build the server project once with `G9SignalRSuperNetCore.Server.ClientInterfaceGenerator` referenced; a typed `ChatHubClient` is generated. Then in the client app:

```csharp
var client = new ChatHubClient("https://localhost:7159");
await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello, world");
Console.ReadLine();
await client.DisconnectAsync();
```

The generated client implements your listener interface with default no-op overrides; subclass it and override the methods you care about:

```csharp
public sealed class MyChatClient(string url) : ChatHubClient(url)
{
    public override Task ReceiveMessage(string user, string message)
    {
        Console.WriteLine($"[{user}] {message}");
        return Task.CompletedTask;
    }

    public override Task UserJoined(string user)
    {
        Console.WriteLine($"{user} joined");
        return Task.CompletedTask;
    }
}
```

---

## Sample with JWT authentication

The library ships a dedicated authentication route (`/AuthHub` by convention) that exchanges arbitrary credentials for a JWT, then a protected hub route (`/SecureHub`) that requires the token. The token is sent on the `access_token` query string so it works over WebSockets out of the box.

**Server — `SecureHub.cs`**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

public class SecureHub : G9AHubBaseWithJWTAuth<SecureHub, IChatClient>
{
    private const string JwtSecret = "replace-with-a-strong-secret-of-at-least-32-bytes";
    private const string Issuer = "G9TM";
    private const string Audience = "G9TM";

    private static readonly G9JWTokenFactory TokenTemplate =
        G9JWTokenFactory.GenerateJWTToken(
            JwtSecret, Issuer, Audience, DateTime.UtcNow.AddDays(3),
            G9ESecurityAlgorithms.HmacSha256);

    public static TokenValidationParameters TokenValidationParameters
        => TokenTemplate.ValidationParameters!;

    public override string RoutePattern() => "/SecureHub";
    public override string AuthAndGetJWTRoutePattern() => "/AuthHub";

    public static Task<(G9JWTokenFactory factory, object? extra)> AuthenticateAsync(
        object authorizeData, Hub authHub)
    {
        // Replace with a real check (DB, Identity, etc.)
        if (authorizeData?.ToString() == "valid-credentials")
        {
            var token = G9JWTokenFactory.GenerateJWTToken(
                JwtSecret, "Iman", "admin", Issuer, Audience, DateTime.UtcNow.AddDays(3));
            return Task.FromResult<(G9JWTokenFactory, object?)>(
                (token, new { Welcome = "Hi Iman" }));
        }

        return Task.FromResult<(G9JWTokenFactory, object?)>(
            (G9JWTokenFactory.RejectAuthorize("Invalid credentials"), null));
    }

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub authHub) => AuthenticateAsync(authorizeData, authHub);

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub()
        => TokenValidationParameters;

    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);
}
```

**Server — `Program.cs`**

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddSignalRSuperNetCoreJwt(
    hubPath: "/SecureHub",
    validationParameters: SecureHub.TokenValidationParameters);

var app = builder.Build();

app.AddSignalRSuperNetCoreJwtHub<SecureHub, IChatClient>(
    hubRoutePattern: "/SecureHub",
    authRoutePattern: "/AuthHub",
    authenticate: SecureHub.AuthenticateAsync);
```

**Client**

```csharp
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

**Define a session**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;

public class ChatSession : G9ASession
{
    public int MessagesSent { get; set; }
    public string? DisplayName { get; set; }
}
```

> Custom fields you add are not automatically thread-safe. The framework manages `ConnectionCounts` and `LastActivityDateTime` atomically; for your own counters use `Interlocked` or a lock.

**Hub with session**

```csharp
public class ChatHubWithSession
    : G9AHubBaseWithSession<ChatHubWithSession, IChatClient, ChatSession>
{
    public override string RoutePattern() => "/chat-session";

    public Task SendMessage(string message)
    {
        Interlocked.Increment(ref _messagesSent); // your own field
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
        object authorizeData, Hub authHub)
    {
        // ... your credential check ...
    }

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
        G9JWTokenFactory.GenerateJWTToken(
            jwtSecret: "replace-me", issuer: "G9TM", audience: "G9TM",
            expires: DateTime.UtcNow.AddDays(3));
}
```

Wire it up:

```csharp
builder.Services.AddSignalRSuperNetCoreCore();
builder.Services.AddG9SignalRSuperNetCoreSessionStore<ChatSession>();
builder.Services.AddSignalRSuperNetCoreJwt(
    AppHub.HubRoute, AppHub.TokenValidationParameters);

var app = builder.Build();

app.AddSignalRSuperNetCoreJwtHub<AppHub, IChatClient>(
    hubRoutePattern: AppHub.HubRoute,
    authRoutePattern: AppHub.AuthRoute,
    authenticate: AppHub.AuthenticateAsync);
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

For horizontal scale-out, replace the registration with a distributed implementation. A Redis-backed store is on the 2.1 roadmap and ships in a separate package so the core library has no Redis dependency.

```csharp
// 2.1 (preview):
builder.Services.AddG9SignalRSuperNetCoreRedisSessionStore<ChatSession>(
    redisConnectionString: builder.Configuration.GetConnectionString("Redis"));
```

## Auto-generated typed client

Add `G9SignalRSuperNetCore.Server.ClientInterfaceGenerator` to your **server** project. After every build, an MSBuild task scans your hubs and writes a `GeneratedClientHelpers.txt` file next to the project containing:

- An `I{HubName}Methods` interface mirroring your public hub methods (`Task` and `Task<T>` signatures).
- An `I{HubName}Listeners` interface mirroring your client interface methods.
- A `{HubName}Client` (or `{HubName}ClientWithJWTAuth`) class that:
  - Wires the connection at the hub's declared route pattern.
  - Provides a typed `Server` proxy implementing `I{HubName}Methods` with concrete `HubConnection.InvokeCoreAsync`/`SendCoreAsync` calls (no Castle, no reflection).
  - Implements `I{HubName}Listeners` with default no-op overrides you can override.
  - Registers all listener callbacks through typed `Connection.On<...>(...)` calls.

Copy the generated content into your client project (or include the file via a build target). Example output:

```csharp
public partial class ChatHubClient :
    G9SignalRSuperNetCoreClient<ChatHubClient, IChatHubMethods, IChatHubListeners>,
    IChatHubListeners
{
    public ChatHubClient(string serverUrl, /* … */) : base($"{serverUrl}/chat", /* … */) { }

    public override IChatHubMethods Server => _serverProxy ??= new ChatHubServerProxy(this);

    protected override void RegisterListenerMethods()
    {
        Connection.On<string, string>(nameof(ReceiveMessage), (u, m) => ReceiveMessage(u, m));
        Connection.On<string>(nameof(UserJoined), u => UserJoined(u));
    }

    public virtual Task ReceiveMessage(string user, string message) => Task.CompletedTask;
    public virtual Task UserJoined(string user) => Task.CompletedTask;
}

internal sealed class ChatHubServerProxy : IChatHubMethods
{
    private readonly ChatHubClient _owner;
    public ChatHubServerProxy(ChatHubClient owner) { _owner = owner; }

    public Task SendMessage(string user, string message)
        => _owner.Connection.SendCoreAsync(nameof(SendMessage),
            new object?[] { user, message });
}
```

The output is fully AOT-safe. Override the listener methods in a subclass to handle inbound messages.

To exclude a hub method from generation:

```csharp
[G9AttrExcludeFromClientGeneration]
public Task InternalDiagnostic() => Task.CompletedTask;
```

To deny a method by policy (always rejected at the auth layer):

```csharp
[G9AttrDenyAccess]
public Task DangerousAction() => Task.CompletedTask;
```

XML doc comments on hub methods and on the client interface are preserved in the generated code.

---

## Client features

### Typed server proxy

```csharp
await client.Server.SendMessage("Iman", "Hi");
List<string> result = await client.Server.GetRecentMessages();
```

Each method on the server interface maps to a single typed call on the underlying `HubConnection`. `Task` methods use `SendCoreAsync`; `Task<T>` methods use `InvokeCoreAsync<T>`. No `MethodInfo.Invoke`, no boxing of return values.

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

Automatic reconnect (`WithAutomaticReconnect()`) is enabled by default and the server timeout is 60 seconds. Both can be customized through the `customConfigureBuilder` and `configureHttpConnection` constructor parameters.

### Connection access

If you need to drop down to raw SignalR APIs (streaming with `IAsyncEnumerable<T>`, manual `On` registrations, etc.), the underlying `HubConnection` is exposed via `client.Connection`.

```csharp
await foreach (var item in client.Connection.StreamAsync<int>("MyStream"))
{
    Console.WriteLine(item);
}
```

### IAsyncDisposable

The base client implements `IAsyncDisposable`. `await using var client = new ChatHubClient(url);` disposes the connection cleanly.

## Attributes

| Attribute | Effect |
|---|---|
| `[G9AttrDenyAccess]` | Applies an authorization policy that always denies. Use to lock framework methods that should not be callable from clients. |
| `[G9AttrExcludeFromClientGeneration]` | Tells the build-time generator to skip a hub method. The method still works on the server, it just doesn't appear in the typed client. |

The framework already applies these to base methods (such as `RoutePattern`, `ConfigureHub`, `AuthAndGetJWTRoutePattern`, lifecycle hooks, session helpers), so they never leak into your generated client.

## JWT helper

`G9JWTokenFactory` covers the common token shapes:

```csharp
// Username + role
var token = G9JWTokenFactory.GenerateJWTToken(
    jwtSecret: secret,
    username: "Iman",
    role: "admin",
    issuer: "G9TM",
    audience: "G9TM",
    expires: DateTime.UtcNow.AddHours(8));

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

A single ASP.NET Core SignalR server typically handles up to ~100,000 WebSocket connections per process under proper tuning. To exceed that and reach hundreds of thousands or millions, scale out across servers:

1. **Pick a backplane.** For most teams the simplest answer is **Azure SignalR Service**; for self-hosted clusters in the same data centre, a **Redis backplane** is fine. Both are first-class ASP.NET Core SignalR scale-out targets and compose cleanly with this library.
2. **Sticky sessions.** With Redis, configure your load balancer for session affinity by `connectionId` or set a connection-aware routing strategy. With Azure SignalR Service, the service handles affinity for you.
3. **Tune the OS.** On Linux: raise `ulimit -n` (file descriptors), increase `net.core.somaxconn`, expand the ephemeral port range, and ensure ports are not exhausted by short-lived backplane connections.
4. **Tune Kestrel.** Set `KestrelServerLimits.MaxConcurrentConnections` and `MaxConcurrentUpgradedConnections` in line with your hardware.
5. **Move session state out of process.** This is what `IG9SessionStore<TSession>` is designed for. The default in-memory store works for a single process; for multi-process clusters, swap in a distributed implementation.

This library does not embed any correctness-critical state in static fields. The same hub source code that runs in a single dev process runs unchanged behind a backplane.

A coming **G9SignalRSuperNetCore.Server.Redis** package will provide a turnkey distributed `IG9SessionStore<TSession>` plus an `AddG9SignalRBackplane(...)` helper that wires `Microsoft.AspNetCore.SignalR.StackExchangeRedis`.

## Thread safety contract

The library is designed for high-concurrency hubs. Specifically:

- **Sessions are linearizable on connect/disconnect.** `G9ASession.ConnectionCounts` is mutated only through `Interlocked.Increment` / `Interlocked.Decrement`. Concurrent reconnect storms from the same user yield a final count equal to (#connects − #disconnects), and the counter is never negative at any observable point.
- **`LastActivityDateTime` never tears.** It is read and written through `Interlocked.Read` / `Interlocked.Exchange` on the underlying `long` ticks. You can read it from any thread and never see a partially-written `DateTime`.
- **Session removal is reference-equality safe.** A session is removed from the in-memory store only if no other connect has raced in and replaced the instance.
- **JWT route registry is read-mostly.** Registrations occur during startup; reads on every authorize call are lock-free `ConcurrentDictionary` lookups.
- **`G9JWTokenFactory` is process-shared.** A single `JwtSecurityTokenHandler` is reused; the handler is documented as thread-safe for issuance and validation.

Custom fields you add to your `TSession` subclass are NOT automatically thread-safe. Synchronize them yourself with `Interlocked` or a lock.

## Build and test

```powershell
# Restore + Release build the whole solution
dotnet build G9SignalRSuperNetCore/G9SignalRSuperNetCore.sln -c Release

# Run the sample web server
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.WebServer -c Release

# Run the sample console client (in another terminal)
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.ConsoleClient -c Release
```

The Release build is warning-clean. CI runs through `azure-pipelines.yml` and publishes the three NuGet packages on `main`.

## Project layout

```
G9SignalRSuperNetCore/
├── G9SignalRSuperNetCore.sln
├── G9SignalRSuperNetCore.Server/                            # NuGet: server library
│   ├── G9SignalRSuperNetCoreServer.cs                       # Add… extension methods
│   ├── Classes/Abstracts/                                   # Hub bases (4 variants) + G9ASession
│   ├── Classes/Attributes/                                  # DenyAccess, ExcludeFromClientGeneration
│   ├── Classes/Helper/                                      # G9JWTokenFactory, deny policy
│   ├── Classes/Hubs/                                        # G9GetJwtHub + JWT route registry
│   ├── Classes/Sessions/                                    # IG9SessionStore + in-memory impl
│   └── Enums/G9ESecurityAlgorithms.cs
├── G9SignalRSuperNetCore.Client/                            # NuGet: client library
│   ├── G9SignalRSuperNetCoreClient.cs                       # AOT-safe base (no Castle)
│   └── G9SignalRSuperNetCoreClientWithJWTAuth.cs            # adds AuthorizeAsync flow
├── G9SignalRSuperNetCore.Server.ClientInterfaceGenerator/   # NuGet: build-time generator
├── G9SignalRSuperNetCore.Server.ClientResourceGenerator/    # MSBuild task implementation
├── G9SignalRSuperNetCore.WebServer/                         # Sample server
└── G9SignalRSuperNetCore.ConsoleClient/                     # Sample client (uses generated helpers)
```

---

## Migration guide (1.x → 2.0)

The 2.0 release is a hardening release. Most consumer code keeps working with small registration changes:

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

   - For unauthenticated hubs:
     ```csharp
     app.AddSignalRSuperNetCoreServerHub<MyHub, IMyClient>(routePattern: "/myhub");
     ```
   - For JWT-authenticated hubs:
     ```csharp
     app.AddSignalRSuperNetCoreJwtHub<MyHub, IMyClient>(
         hubRoutePattern: MyHub.HubRoute,
         authRoutePattern: MyHub.AuthRoute,
         authenticate:    MyHub.AuthenticateAsync);
     ```

   Reading the route pattern from a synthesized instance required `Reflection.Emit`-style instance creation, which is incompatible with NativeAOT and full trim. Passing routes explicitly removes that dependency.

3. **Session lifecycle.** The static per-process dictionary on `G9AHubBaseWithSession*` is gone. Behavior is the same; the store comes from DI now. If your code reached into the framework's internals (it shouldn't have), the old `HubSessionStore` static field no longer exists.

4. **Client proxy.** The runtime Castle DynamicProxy proxy is replaced by the build-time generator output. Regenerate your client by rebuilding the server project, then copy the new `GeneratedClientHelpers.txt` into your client project (or wire up the file as a build artifact). Override the listener methods on the generated class to receive callbacks.

5. **Client helpers.** `AssignListenerEvent`, `ListenOnceAsync`, and `SendThenListenOnceAsync` from 1.x are removed. They were heavy in reflection and not AOT-safe. Equivalent patterns:
   - To register a listener: override the typed listener method on the generated client subclass.
   - To wait once for a server callback: use `Connection.On<T>` to register a `TaskCompletionSource<T>` handler, then `await` the TCS.

6. **`Connection` is still public.** Nothing prevents you from calling `client.Connection.InvokeAsync` directly when you need to.

The Castle.Core dependency is no longer present. Remove any direct references you had to it from your client project.

## Roadmap

The 2.x line is shipped as a sequence of focused milestones.

- **2.0 — Foundation (this release).** AOT/MAUI safety, source-generator typed client, pluggable session store, lock-free session counters, encapsulated JWT registry, warning-clean Release build.
- **2.1 — Security and policy.**
  - `[G9AttrRequireSecureChannel]` — application-layer encryption built on `ECDH P-256` + `HKDF-SHA-256` + `AES-GCM` / `ChaCha20-Poly1305`, with public-key pinning, per-direction monotonic nonces, and replay protection. Designed for environments where TLS is unavailable or untrusted; explicitly not a multi-party end-to-end protocol.
  - `[G9AttrRateLimit(perSecond, burst)]` — per-method rate limiting with a stable error code.
  - `[G9AttrConnectionRequired]` — reject calls on connections not in the `Connected` state with a clear error.
  - `[G9AttrTelemetry(name)]` — `ActivitySource` traces around hub method execution.
  - `IG9HubMetrics` interface for plugging in custom counters.
- **2.2 — Distributed scale-out.**
  - `G9SignalRSuperNetCore.Server.Redis` — Redis-backed `IG9SessionStore<TSession>` and `AddG9SignalRBackplane(...)` helper.
  - Property-based tests for session linearizability, JWT round-trip, and the secure-channel invariants.
  - BenchmarkDotNet project measuring per-call throughput and allocation across protocol options.

The order can shift in response to consumer feedback; track progress in the issues board.

## Contributing

Issues and pull requests are welcome.

1. Open an issue describing the change before sending a large PR.
2. Match the existing code style: file-scoped namespaces, XML docs on public members, `G9` prefix on public types.
3. `dotnet build -c Release` must finish with zero warnings — including AOT and trim warnings attributable to G9 code.
4. Update or add a sample under `G9SignalRSuperNetCore.WebServer` / `G9SignalRSuperNetCore.ConsoleClient` when adding new public surface.
5. New cryptographic code (in 2.1+) must use BCL primitives only and ship with property-based tests.

## License

Released under the [MIT License](LICENSE.md).
