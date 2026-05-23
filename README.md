# G9SignalRSuperNetCore

[![NuGet](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.svg?style=flat-square&label=Server)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server/)
[![NuGet](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Client.svg?style=flat-square&label=Client)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Client/)
[![NuGet](https://img.shields.io/nuget/v/G9SignalRSuperNetCore.Server.ClientInterfaceGenerator.svg?style=flat-square&label=ClientInterfaceGenerator)](https://www.nuget.org/packages/G9SignalRSuperNetCore.Server.ClientInterfaceGenerator/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

**G9SignalRSuperNetCore** is a strongly-typed, productivity-focused wrapper around ASP.NET Core SignalR that lets you build real-time hubs and clients with less ceremony and more safety. It bundles three things most SignalR projects end up reinventing: a typed hub/client base, a JWT authentication flow, and per-connection session state — plus a build-time generator that produces a typed client from your hubs automatically.

> Server hubs and clients talk through interfaces. Client method calls become `Task` / `Task<T>` invocations. Server-to-client callbacks are wired up by reflection and exposed as listener methods. Authentication, reconnect, and session lifecycle are handled in the base classes.

---

## Table of Contents

- [Why this library](#why-this-library)
- [Packages](#packages)
- [Architecture overview](#architecture-overview)
- [Getting started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Install](#install)
- [Quick sample (no auth)](#quick-sample-no-auth)
- [Sample with JWT authentication](#sample-with-jwt-authentication)
- [Sample with sessions](#sample-with-sessions)
- [Sample with JWT + sessions (recommended)](#sample-with-jwt--sessions-recommended)
- [Auto-generated client helpers](#auto-generated-client-helpers)
- [Client features](#client-features)
- [Attributes](#attributes)
- [JWT helper](#jwt-helper)
- [Build and test](#build-and-test)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)

---

## Why this library

Plain SignalR works, but you usually end up writing the same plumbing: a typed proxy for server methods, a registration block for client callbacks, a JWT pipeline that plays nicely with WebSockets, a per-connection session, and a code-gen step so the client and server never drift. G9SignalRSuperNetCore covers all of that:

- **Strongly-typed server hubs** with a generic `Hub<TClientInterface>` base
- **Strongly-typed client proxy** generated at runtime via Castle DynamicProxy — call `client.Server.MyMethod(...)` directly
- **Automatic listener wiring** — client implements the listener interface and base class hooks the methods up
- **JWT authentication out of the box** — separate "auth hub" route exchanges credentials for a token, then the protected hub uses `[Authorize]`
- **Per-connection session** — thread-safe session store with first-connect / last-activity tracking and cleanup helpers
- **Build-time client generator** — drop a NuGet reference into your server project and the typed client is generated from your hub signatures
- **Streaming and request/response helpers** — `ListenOnceAsync`, `SendThenListenOnceAsync`, and `IAsyncEnumerable` streaming all supported

## Packages

| Package | Purpose |
|---|---|
| `G9SignalRSuperNetCore.Server` | Hub base classes, JWT pipeline, session store, attributes, helpers |
| `G9SignalRSuperNetCore.Client` | Typed client base classes for both anonymous and JWT-authenticated hubs |
| `G9SignalRSuperNetCore.Server.ClientInterfaceGenerator` | MSBuild task that generates a typed client from your hubs |

All packages target **.NET 10.0**.

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
│   AddSignalRSuperNetCoreServerService<...>()                         │
│   AddSignalRSuperNetCoreServerHub<...>()                             │
│                                                                      │
│   JWT auth → /AuthHub  ──►  protected hub at /SecureHub              │
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
│   client.Server.MyMethod(args)        ── strongly-typed proxy        │
│   listener methods on derived class   ── auto-wired                  │
│   client.AssignListenerEvent(...)     ── lambda-style listener       │
│   client.ListenOnceAsync(...)         ── one-shot await              │
│   client.SendThenListenOnceAsync(...) ── request/response            │
└─────────────────────────────────────────────────────────────────────┘
```

## Getting started

### Prerequisites

- .NET 10.0 SDK or later
- An ASP.NET Core 10 project for the server
- Any .NET 10 project for the client (console, WPF, MAUI, Blazor, ASP.NET, etc.)

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

The `ClientInterfaceGenerator` package is optional. Use it when you want a typed client class generated from your hub signatures at build time. See [Auto-generated client helpers](#auto-generated-client-helpers).

---

## Quick sample (no auth)

A minimal hub, a client interface for server-to-client callbacks, and a console client that connects.

**Server — `Program.cs`**

```csharp
using G9SignalRSuperNetCore.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalRSuperNetCoreServerService<ChatHub, IChatClient>();

var app = builder.Build();

app.AddSignalRSuperNetCoreServerHub<ChatHub, IChatClient>();
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

**Client — `ChatClient.cs`** (typed; written by hand or generated)

```csharp
using G9SignalRSuperNetCore.Client;

public interface IChatHubMethods
{
    Task SendMessage(string user, string message);
}

public interface IChatHubListeners
{
    Task ReceiveMessage(string user, string message);
    Task UserJoined(string user);
}

public class ChatClient : G9SignalRSuperNetCoreClient<ChatClient, IChatHubMethods, IChatHubListeners>,
    IChatHubListeners
{
    public ChatClient(string serverUrl) : base($"{serverUrl}/chat") { }

    public Task ReceiveMessage(string user, string message)
    {
        Console.WriteLine($"[{user}] {message}");
        return Task.CompletedTask;
    }

    public Task UserJoined(string user)
    {
        Console.WriteLine($"{user} joined");
        return Task.CompletedTask;
    }
}
```

**Client — `Program.cs`**

```csharp
var client = new ChatClient("https://localhost:7159");
await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello, world");
Console.ReadLine();
await client.DisconnectAsync();
```

---

## Sample with JWT authentication

The library exposes a dedicated authentication route (default `/AuthHub`) that exchanges arbitrary credentials for a JWT, then a protected hub route (default `/SecureHub`) that requires the token. The JWT is sent over the `access_token` query string so it works with WebSockets out of the box.

**Server — `SecureHub.cs`**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

public class SecureHub : G9AHubBaseWithJWTAuth<SecureHub, IChatClient>
{
    private const string JwtSecret = "replace-with-a-strong-secret-of-at-least-32-bytes-please-do-not-ship-this";

    private static readonly G9JWTokenFactory TokenTemplate =
        G9JWTokenFactory.GenerateJWTToken(
            JwtSecret,
            issuer: "G9TM",
            audience: "G9TM",
            expires: DateTime.UtcNow.AddDays(3),
            securityAlgorithm: G9ESecurityAlgorithms.HmacSha256);

    public override string RoutePattern() => "/SecureHub";
    public override string AuthAndGetJWTRoutePattern() => "/AuthHub";

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub authHub)
    {
        // Replace this with a real credentials check (DB, Identity, etc.)
        if (authorizeData?.ToString() == "valid-credentials")
        {
            var token = G9JWTokenFactory.GenerateJWTToken(
                JwtSecret, username: "Iman", role: "admin",
                issuer: "G9TM", audience: "G9TM",
                expires: DateTime.UtcNow.AddDays(3));
            return Task.FromResult<(G9JWTokenFactory, object?)>((token, new { Welcome = "Hi Iman" }));
        }

        return Task.FromResult<(G9JWTokenFactory, object?)>(
            (G9JWTokenFactory.RejectAuthorize("Invalid credentials"), null));
    }

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub()
        => TokenTemplate.ValidationParameters!;

    public Task SendMessage(string user, string message)
        => Clients.All.ReceiveMessage(user, message);
}
```

**Client**

```csharp
var client = new SecureHubClient("https://localhost:7159"); // generated, see below

var auth = await client.AuthorizeAsync("valid-credentials");
if (!auth.IsAccepted)
{
    Console.WriteLine($"Auth rejected: {auth.RejectionReason}");
    return;
}

await client.ConnectAsync();
await client.Server.SendMessage("Iman", "Hello secure world");
```

`AuthorizeAsync` returns a `G9DtAuthorizeResult` with `IsAccepted`, `JWToken`, `RejectionReason`, and `ExtraData`. The token is cached internally and used automatically when you call `ConnectAsync()`. You can also pass an existing token explicitly via `ConnectAsync(string jwToken)`.

---

## Sample with sessions

`G9AHubBaseWithSession<THub, TClient, TSession>` adds a thread-safe session store keyed by user identifier (or connection id when there is no user). Multiple connections from the same user share one session and a connection counter, which makes "Is user X online?" cheap.

**Define a session**

```csharp
using G9SignalRSuperNetCore.Server.Classes.Abstracts;

public class ChatSession : G9ASession
{
    public int MessagesSent { get; set; }
    public string? DisplayName { get; set; }
}
```

**Hub with session**

```csharp
public class ChatHubWithSession
    : G9AHubBaseWithSession<ChatHubWithSession, IChatClient, ChatSession>
{
    public override string RoutePattern() => "/chat-session";

    public Task SendMessage(string message)
    {
        Session.MessagesSent++;
        return Clients.All.ReceiveMessage(Session.DisplayName ?? "anon", message);
    }

    public bool IsOnline(string userId) => IsUserConnected(userId);
}
```

You can call `ChatHubWithSession.CleanupExpiredSessions(TimeSpan.FromMinutes(30))` from a background job to evict idle sessions.

---

## Sample with JWT + sessions (recommended)

For most production hubs, you want both. `G9AHubBaseWithSessionAndJWTAuth` combines them, and the auth user identifier is used as the session key automatically.

```csharp
public class AppHub
    : G9AHubBaseWithSessionAndJWTAuth<AppHub, IChatClient, ChatSession>
{
    private readonly ILogger<AppHub> _logger;

    public AppHub(ILogger<AppHub> logger) => _logger = logger;

    public override string RoutePattern() => "/SecureHub";
    public override string AuthAndGetJWTRoutePattern() => "/AuthHub";

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub authHub) => /* same as JWT sample */ ...;

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub() => ...;

    public async Task<List<string>> GetRecentMessages()
    {
        Session.MessagesSent++;
        return await Task.FromResult(new List<string> { "msg1", "msg2" });
    }
}
```

Register and map:

```csharp
builder.Services.AddSignalRSuperNetCoreServerService<AppHub, IChatClient>();
// ...
app.AddSignalRSuperNetCoreServerHub<AppHub, IChatClient>();
```

---

## Auto-generated client helpers

Add `G9SignalRSuperNetCore.Server.ClientInterfaceGenerator` to your **server** project. After every build, an MSBuild task scans your hubs and writes a `GeneratedClientHelpers.txt` file next to your project. It contains:

- A `I{HubName}Methods` interface mirroring your public hub methods
- A `I{HubName}Listeners` interface mirroring the client interface methods
- A `{HubName}Client` (or `{HubName}ClientWithJWTAuth`) class wired to the right base class and route pattern

Copy the generated content into your client project (or include the file via a build target). You get a strongly typed client like:

```csharp
public class CustomHubWithJWTAuthAndSessionClientWithJWTAuth :
    G9SignalRSuperNetCoreClientWithJWTAuth<
        CustomHubWithJWTAuthAndSessionClientWithJWTAuth,
        ICustomHubWithJWTAuthAndSessionMethodsWithJWTAuth,
        ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth>,
    ICustomHubWithJWTAuthAndSessionListenersWithJWTAuth
{
    public CustomHubWithJWTAuthAndSessionClientWithJWTAuth(string serverUrl, string? jwToken = null, ...)
        : base($"{serverUrl}/SecureHub", $"{serverUrl}/AuthHub", jwToken, ...) { }

    public Task LoginResult(bool accepted) { /* default impl */ }
    public Task ReceiveMessage(string user, string message) { /* default impl */ }
}
```

XML doc comments on hub methods and on the client interface are preserved in the generated code.

To exclude a public hub method from generation:

```csharp
[G9AttrExcludeFromClientGeneration]
public Task InternalDiagnostic() => Task.CompletedTask;
```

To prevent the client from invoking a method server-side at all (deny by policy):

```csharp
[G9AttrDenyAccess]
public Task DangerousAction() => Task.CompletedTask;
```

---

## Client features

### Typed server proxy

```csharp
await client.Server.SendMessage("Iman", "Hi");
List<string> result = await client.Server.GetRecentMessages();
```

A Castle DynamicProxy implements your server-methods interface. `Task` and `Task<T>` are supported; other return types throw `NotSupportedException`.

### Listener wiring

Implement the listener interface on your client class and the base wires up `Connection.On(...)` for every method via reflection. Up to 8 parameters are supported.

### Lambda listeners

```csharp
client.AssignListenerEvent(
    s => s.ReceiveMessage,
    (string user, string message) =>
    {
        Console.WriteLine($"[{user}] {message}");
        return Task.CompletedTask;
    });
```

### Request/response helpers

```csharp
// Wait for a one-shot callback (default 1-minute timeout)
var (a, b) = await client.ListenOnceAsync<string, string>(s => s.TestResult);

// Send and wait for the matching callback in one call (no race conditions)
var result = await client.SendThenListenOnceAsync<string, string>(
    sendPart => sendPart.TestResult("Test1", "Test2"),
    s => s.TestResult);
```

### Streaming

If a listener method on your interface returns `IAsyncEnumerable<T>`, the client opens a stream channel via `Connection.StreamAsChannelCoreAsync` and dispatches each item to your handler.

### Reconnect and timeouts

Automatic reconnect is enabled by default (`WithAutomaticReconnect()`), and the server timeout is 60 seconds. Both can be customized through `customConfigureBuilder` on the constructor.

---

## Attributes

| Attribute | Effect |
|---|---|
| `[G9AttrDenyAccess]` | Applies an authorization policy that always denies. Use to lock down internal methods. |
| `[G9AttrExcludeFromClientGeneration]` | Tells the client generator to skip a hub method. |

The framework already applies these to base methods (like `RoutePattern`, `ConfigureHub`, `AuthAndGetJWTRoutePattern`, etc.), so they never leak into your generated client.

## JWT helper

`G9JWTokenFactory` covers most token shapes you need:

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
    new[] { new Claim("plan", "pro") }, expires: DateTime.UtcNow.AddDays(1));

// Reject the request
var rejected = G9JWTokenFactory.RejectAuthorize("Invalid credentials");
```

Supported algorithms (`G9ESecurityAlgorithms`): `HmacSha256/384/512`, `RsaSha256/384/512`, `RsaSsaPssSha256/384/512`, `EcdsaSha256/384/512`, `Aes128/192/256KW`, `RsaOAEP`, `Rsa1_5`, `None`.

---

## Build and test

This repository uses the standard .NET CLI:

```powershell
# Restore + build everything in Release
dotnet build G9SignalRSuperNetCore/G9SignalRSuperNetCore.sln -c Release

# Run the sample web server
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.WebServer -c Release

# Run the sample console client (in another terminal)
dotnet run --project G9SignalRSuperNetCore/G9SignalRSuperNetCore.ConsoleClient -c Release
```

The Release build is warning-clean. CI runs through `azure-pipelines.yml` and publishes the three NuGet packages on master.

## Project layout

```
G9SignalRSuperNetCore/
├── G9SignalRSuperNetCore.sln
├── G9SignalRSuperNetCore.Server/                       # NuGet: server library
│   ├── G9SignalRSuperNetCoreServer.cs                  # AddSignalRSuperNetCoreServerService / Hub
│   ├── Classes/Abstracts/                              # G9AHubBase, JWT, session bases
│   ├── Classes/Attributes/                             # DenyAccess, ExcludeFromClientGeneration
│   ├── Classes/Helper/                                 # G9JWTokenFactory, deny policy
│   ├── Classes/Hubs/                                   # G9GetJwtHub (auth route)
│   └── Enums/G9ESecurityAlgorithms.cs
├── G9SignalRSuperNetCore.Client/                       # NuGet: client library
│   ├── G9SignalRSuperNetCoreClient.cs                  # base client + proxy + listeners
│   └── G9SignalRSuperNetCoreClientWithJWTAuth.cs       # adds AuthorizeAsync flow
├── G9SignalRSuperNetCore.Server.ClientInterfaceGenerator/  # NuGet: build-time generator
├── G9SignalRSuperNetCore.Server.ClientResourceGenerator/   # MSBuild task implementation
├── G9SignalRSuperNetCore.WebServer/                    # Sample server
└── G9SignalRSuperNetCore.ConsoleClient/                # Sample client (uses generated helpers)
```

## Contributing

Issues and pull requests are welcome. A few guidelines that keep things smooth:

1. Open an issue describing the change before sending a large PR.
2. Match the existing code style (file-scoped namespaces, XML docs on public members, `G9` prefix on public types).
3. Make sure `dotnet build -c Release` finishes with zero warnings.
4. Update or add a sample under `G9SignalRSuperNetCore.WebServer` / `G9SignalRSuperNetCore.ConsoleClient` when adding new public surface.

## License

Released under the [MIT License](LICENSE.md).
