# Research Summary

This document captures the deep-research findings the user requested before writing requirements. It is informational only — concrete implementation choices live in `design.md`.

## 1. SignalR 10 / .NET 10 — current state

- ASP.NET Core SignalR 10 (server + `Microsoft.AspNetCore.SignalR.Client` 10.0.x) "supports trimming and native ahead-of-time (AOT) compilation for supported scenarios" (Microsoft, [SignalR Overview – aspnetcore-10.0](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)). Content rephrased for compliance with licensing restrictions.
- Tracking issue [dotnet/aspnetcore #59133 — "Does Microsoft.AspNetCore.SignalR.Client support AOT?"](https://github.com/dotnet/aspnetcore/issues/59133) shows the client *can* be used under AOT, but every `On<T>` / `InvokeAsync<T>` call still flows through the JSON / MessagePack pipeline, so the consumer is responsible for providing trim-safe serializer hooks (e.g. `JsonSerializerContext`).
- Default `JsonHubProtocol` is reflection-based and emits `IL2026` / `IL3050` warnings under `PublishTrimmed` / `PublishAot`. The fix is to register a `JsonSerializerContext` (System.Text.Json source-generated) or use `MessagePackHubProtocol` with `MessagePack.SourceGenerator` (v3+, ships analyzer that flags AOT-unsafe types).
- The strongly-typed *server* side already exists: `Hub<TClient>` is fine. The strongly-typed *client* side does not ship in the box; the de-facto solution is a Roslyn source generator. [TypedSignalR.Client](https://github.com/nenoNaninu/TypedSignalR.Client) is the closest reference and is AOT-friendly because it emits real C# code at compile time (no reflection, no `Reflection.Emit`).

## 2. Castle.Core / DynamicProxy under MAUI + AOT

- `Castle.DynamicProxy` is built on `System.Reflection.Emit`. `Reflection.Emit` is unsupported on iOS (no JIT permitted) and unsupported under NativeAOT in general. Multiple ecosystem references confirm this: [Stl.Fusion #67](https://github.com/servicetitan/Stl.Fusion/issues/67), [efcore #12393](https://github.com/dotnet/efcore/issues/12393). Content paraphrased for licensing compliance.
- Practical consequence for this repo: `G9SignalRSuperNetCoreClient.CreateServerProxy` will throw at runtime on iOS and will fail to publish under `PublishAot=true`. This is a hard blocker for MAUI, not a warning.
- Replacement pattern is well-trodden: emit the proxy as a real C# class via a Roslyn source generator that the consumer's project compiles. StreamJsonRpc ([NativeAOT / Trimming](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html)), MagicOnion ([Cysharp/MagicOnion](https://github.com/Cysharp/MagicOnion)), and TypedSignalR.Client all use this approach.

## 3. Reflection on hot paths

The current implementation does reflection in three places that matter:
- `G9SignalRSuperNetCoreClient.RegisterClientMethods()` — once per connection at startup. Acceptable cost, but **AOT-hostile**: `typeof(TClientListenerMethods).GetMethods()` and `Connection.On(name, types[], handler)` blow up under trimming because the parameter types may have been trimmed.
- `ServerMethodInterceptor.Intercept(...)` — **every server call**. Allocates an `IInvocation`, looks up `MethodInfo`, calls a generic `MakeGenericMethod` + `Invoke`. Both an AOT problem and a perf problem on a hot path.
- `CreateTuple(args)` in `ListenOnceCore` — uses `Type.GetType("System.ValueTuple\`N")`, `MakeGenericType`, `Activator.CreateInstance`. AOT-hostile.

Source-generated proxies + source-generated dispatch tables eliminate all three. This is the pattern MagicOnion uses for Unity IL2CPP and matches what we need for MAUI iOS / NativeAOT.

## 4. Scale to "millions of connections"

- Per-process limits for ASP.NET Core SignalR are documented in [SignalR production hosting and scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0). A single Kestrel process typically tops out in the tens-of-thousands of WebSocket connections, bound by file descriptors, ephemeral ports, kernel buffers, and CPU.
- Reaching millions requires horizontal scale. Two supported topologies:
  - [Redis backplane](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0) (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) for self-hosted scenarios.
  - [Azure SignalR Service](https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-overview) when running in Azure — currently advertised up to 100 units, supporting millions of concurrent connections.
- **Library-level requirement:** the library MUST NOT make any horizontal-scale topology *impossible*. Today, `G9AHubBaseWithSession` keeps sessions in a process-local `static ConcurrentDictionary`, which is acceptable for single-process deployments but means session state is lost on failover and cannot be shared across nodes. The library should make the session store pluggable so consumers can plug Redis / a distributed cache.
- **Sticky sessions** are required for SignalR over long polling and for the negotiate handshake. This is environmental (load balancer config), not library code, but must be documented.

## 5. Thread safety of current code

Audit findings (will be encoded as requirements / tasks, not fixed inline):

1. **`G9AHubBaseWithSession` and `G9AHubBaseWithSessionAndJWTAuth` duplicate the same code verbatim.** Two static dictionaries keyed by `typeof(TTargetClass).GUID`. This is a maintenance bug, not a thread-safety bug, but enables drift.
2. **`OnConnectedAsync` mutates `existingSession.ConnectionCounts` and `LastActivityDateTime` inside `AddOrUpdate`'s `updateValueFactory`.** `ConcurrentDictionary` documents that `updateValueFactory` may run multiple times under contention, and the mutation is `++` on a non-volatile `int`. Two simultaneous connects from the same user can race and lose an increment, leading to `ConnectionCounts` drifting. The session is then prematurely removed in `OnDisconnectedAsync` because the count goes to zero "early." Fix: use `Interlocked.Increment` on a volatile field, or pre-construct an immutable session record and replace via CAS.
3. **`OnDisconnectedAsync` decrements without `Interlocked` and then conditionally removes.** Same race; the read-decide-remove window is not atomic. A connect arriving between the decrement and the `TryRemove` would see a zero-count session that's about to be deleted out from under it.
4. **`G9ASession.ConnectionCounts` and `LastActivityDateTime` are exposed with `internal set`** but are mutated directly from outside the class. This couples the invariant ("counts >= 0") to whoever calls the setter. The fix is to encapsulate the mutations on `G9ASession` and document the invariants.
5. **`G9GetJwtHub._validateUserAndGenerateJWTokenPerRoute`** is correctly a `ConcurrentDictionary`, but the field name violates conventions (leading underscore on a `static internal`) and there's no API to remove a route — registration is permanent for the process lifetime. Acceptable but should be documented.

## 6. Performance hot spots

- Per-call `object[]` argument boxing in `Connection.On(name, types, handler)` and in the proxy interceptor — unavoidable with the current SignalR client API surface, but a source-generator proxy can call the typed `InvokeAsync<T>(...)` overloads directly, avoiding the generic `InvokeCoreAsync` path.
- `JwtSecurityTokenHandler` is the legacy JWT handler. The newer `JsonWebTokenHandler` is the recommended high-performance path in [Microsoft.IdentityModel](https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet) and is materially faster at high RPS.
- `DateTime.Now` in `OnConnectedAsync` / `OnDisconnectedAsync` allocates a TimeZoneInfo lookup on every call. `DateTime.UtcNow` is significantly cheaper and is the right choice for "last activity" timestamps.
- Default `JsonHubProtocol` allocates per call; `MessagePackHubProtocol` is denser on the wire and faster to parse. For high-throughput clients, MessagePack with the source-generated formatter is the right default.

## 7. App-level "secure without SSL" channel

The user's intuition — "client receives a public key on first connection, then works with that" — is exactly the [Noise Protocol Framework](https://noiseprotocol.org/noise.html), specifically the `IK` and `XX` patterns. WireGuard uses Noise IK in production for the same threat model.

Findings:

- **Trust model.** A public key fetched over the same insecure channel that you're trying to secure can be MITM'd. The standard mitigations are:
  - **Public-key pinning (preferred):** the server's static public key is embedded in the client at build time (or fetched once over a trusted channel — TLS, app store update). This is what WireGuard does.
  - **Trust-On-First-Use (TOFU):** the client persists the public key it saw on first connection and refuses to accept a different key on subsequent connections. Acceptable for device-to-server scenarios where the first connection happens on a trusted network.
  - The library must support **both** modes and must default to *pinned*; TOFU must be opt-in with a documented warning.
- **Handshake.** Noise `IK` (initiator knows responder's static key) gives mutual authentication, forward secrecy, and 1-RTT. Noise `XX` adds responder-static-key transmission for TOFU. .NET 10 has native `ECDiffieHellman` (NIST curves) and `System.Security.Cryptography.X25519` keypair support — the [`X25519`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x25519) primitive is available. ECDH + HKDF for key agreement is BCL-supported, AOT-safe, and has no managed-code crypto we'd be writing ourselves.
- **AEAD.** Two viable choices in `System.Security.Cryptography`: `AesGcm` and `ChaCha20Poly1305`.
  - `AesGcm.IsSupported` and `ChaCha20Poly1305.IsSupported` both exist in .NET 10 — the runtime tells us at startup whether the platform has the primitive.
  - Performance ([Tuning TLS: AES-256 Beats ChaCha20 on Every CPU, 2025](https://ashvardanian.com/posts/chacha-vs-aes-2025/), content paraphrased): on modern CPUs with AES-NI / VAES (x86) or AES + PMULL (ARMv8/ARMv9), AES-256-GCM now outperforms ChaCha20-Poly1305 by a wide margin. On older mobile / embedded ARM without AES instructions, ChaCha20-Poly1305 is faster and side-channel-safer.
  - Conclusion: support both, prefer `AesGcm` when the platform reports hardware acceleration, otherwise prefer `ChaCha20Poly1305`. Both are first-class in `System.Security.Cryptography` and are AOT-safe.
- **Replay protection.** AEAD nonce is sequence-number-based (96-bit nonce = 32-bit fixed prefix from handshake + 64-bit monotonically-increasing counter). Receiver enforces strict-greater-than on the counter. Rekey before counter wraps (well before 2^64).
- **Forward secrecy.** Achieved by using *ephemeral* ECDH keypairs in the handshake. Static keys authenticate; ephemeral keys provide PFS.
- **Layering on SignalR.** The plaintext SignalR hub-protocol payload is a `byte[]` / `ReadOnlyMemory<byte>` once serialized. The simplest integration: a `WrappedHubProtocol` that wraps an inner `IHubProtocol` (Json or MessagePack) and AEAD-encrypts the produced bytes on send / decrypts on receive. Frame format: `nonce (8 bytes) | ciphertext | tag (16 bytes)`. This keeps the encryption *transport-agnostic* — works equally over WebSockets, SSE, long-polling.
- **Critical disclaimer.** This is defense-in-depth. It does NOT replace TLS for typical web deployments. Use cases where it's genuinely useful: device-to-server traffic in environments without a CA trust chain (industrial, IoT), legacy networks where TLS termination at a corporate proxy would expose plaintext, layered confidentiality where the WebSocket reverse proxy is *also* untrusted. Documentation must state this clearly so consumers don't disable TLS thinking this is a substitute.

## 8. Summary of plan to bring to the user

1. **Replace `Castle.DynamicProxy` with a Roslyn source generator** that emits a real C# proxy class implementing `TServerHubMethods`. This is the lynchpin change — it fixes MAUI iOS, NativeAOT, AND removes the per-call reflection overhead.
2. **Replace reflection-based `RegisterClientMethods`** with source-generated typed `Connection.On<T1, T2, …>(...)` registrations.
3. **Replace the build-time `Microsoft.Build.Utilities.Task` (G9HubClientGeneratorTask) with an `IIncrementalGenerator`.** Same output, integrated with the standard Roslyn pipeline, no `.txt` artifact, runs on edit, AOT-friendly, no Roslyn DLL shipping.
4. **Make session storage pluggable** via an interface; keep the in-process `ConcurrentDictionary` as the default; provide an extension point for Redis or `IDistributedCache`. Fix the increment/decrement races with `Interlocked`.
5. **Provide MessagePack as a first-class option** with `MessagePack.SourceGenerator`. Keep JSON as the default but require consumers to register a `JsonSerializerContext` when targeting AOT.
6. **Add an opt-in app-level secure channel** as a wrapping `IHubProtocol`: Noise-IK-style handshake (public-key-pinned, with TOFU as opt-in), ECDH for key agreement, AEAD (AES-GCM preferred when hardware-accelerated, ChaCha20-Poly1305 fallback), strict nonce counter, rekey policy, replay rejection. This is on top of, not instead of, TLS.
7. **Add attribute hooks** for: per-method rate limit, per-connection rate limit, telemetry/metrics emission, and replay protection on hub methods (idempotency-key style).
8. **Migrate JWT helper to `JsonWebTokenHandler`** for better throughput; keep the existing API surface backward-compatible.
9. **Document the scale-out story**: pluggable session store + Redis/Azure SignalR backplane + sticky-session requirement + connection-cap guidance.
10. **Backward compatibility:** existing consumers' hubs (`G9AHubBase` etc.) keep working. The reflection-based client and Castle proxy are kept available behind a non-AOT-targeted flag for one major version, then removed.

The above will be expressed as user stories + EARS acceptance criteria in `requirements.md`. Specific implementation choices (Noise IK vs XX, AES-GCM vs ChaCha20, pluggable session interface shape) are deferred to `design.md`.

---

Key references (for design and verification phases):
- [SignalR overview, aspnetcore-10.0](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0)
- [SignalR scale, aspnetcore-10.0](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0)
- [Redis backplane for SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0)
- [How to make libraries compatible with native AOT](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)
- [TypedSignalR.Client – source generator reference](https://github.com/nenoNaninu/TypedSignalR.Client)
- [StreamJsonRpc — NativeAOT / Trimming docs](https://microsoft.github.io/vs-streamjsonrpc/docs/nativeAOT.html)
- [MagicOnion (Cysharp)](https://github.com/Cysharp/MagicOnion)
- [Noise Protocol Framework](https://noiseprotocol.org/noise.html)
- [ChaCha20Poly1305 in System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.chacha20poly1305)
- [Tuning TLS: AES-256 Beats ChaCha20 on Every CPU, 2025](https://ashvardanian.com/posts/chacha-vs-aes-2025/) (paraphrased)
