# Requirements Document

## Introduction

G9SignalRSuperNetCore today is a productive wrapper around ASP.NET Core SignalR. It works in process on Windows and Linux servers, but it has hard blockers that prevent it from running on .NET MAUI clients (iOS in particular), prevent NativeAOT publishing, and prevent horizontal scale beyond a single process. The hot paths use runtime reflection and a Castle DynamicProxy interceptor, so each invocation costs more than it should at high connection counts. The session store is process-local static state, which silently breaks correctness behind a load balancer. There is also a stated need to support a confidential transport when TLS is unavailable.

This effort hardens the library across six themes:

1. **MAUI / NativeAOT compatibility** — eliminate `Reflection.Emit`, replace runtime proxies with a build-time source generator, annotate trim safety, and verify on Android, iOS, MacCatalyst, and Windows targets.
2. **Performance** — remove allocations and reflection from per-call hot paths, support `ValueTask`, pool buffers, and offer MessagePack as an opt-in protocol.
3. **Scale** — make the library safe for horizontal scale-out (Redis backplane and Azure SignalR Service), remove process-local state from the correctness contract, and document concrete scaling guidance.
4. **Thread safety** — audit and fix races in session lifecycle and JWT route registration; document invariants with property-based tests.
5. **Attribute / feature surface** — add per-method rate limiting, per-connection / per-user / per-IP connection caps, replay protection, telemetry hooks, and structured logging.
6. **Application-layer secure channel without TLS** — implement an opt-in **Noise IK / XX**–based handshake on top of SignalR using only BCL crypto primitives, with explicit public-key pinning and a clear operator-facing security model.

Backward compatibility for existing consumers is a stated goal: an upgrade path that keeps current public types working through one major version is required.

## Glossary

- **AEAD** — Authenticated Encryption with Associated Data (e.g., AES-256-GCM, ChaCha20-Poly1305).
- **AOT** — Ahead-Of-Time compilation. NativeAOT (.NET) and full AOT (MAUI iOS) are the two relevant flavors.
- **Backplane** — A pub/sub layer (typically Redis or Azure SignalR Service) that fans out hub messages across server instances.
- **DH** — Diffie–Hellman. **X25519** is the elliptic-curve variant we will use.
- **E2E (within session)** — Encryption between an authenticated client and the server, not multi-party end-to-end. The server can decrypt; this is a transport security replacement, not a ban on server access.
- **HKDF** — HMAC-based Key Derivation Function.
- **Hub** — An ASP.NET Core SignalR server endpoint. A class derived from `Hub<TClient>`.
- **IK / XX** — Two named handshake patterns from the **Noise Protocol Framework**. IK assumes the initiator already knows the responder's static public key; XX has both sides exchange and authenticate static keys during the handshake.
- **JWT** — JSON Web Token, used today for SignalR hub authorization.
- **Listener method** — A method on the client class that the server invokes (server-to-client callback).
- **MAUI** — .NET Multi-platform App UI. Targets Android, iOS, MacCatalyst, and Windows.
- **MITM** — Man-in-the-Middle attack.
- **PBT** — Property-Based Testing.
- **Proxy method** — A method the client uses to invoke a server hub method (client-to-server call).
- **Replay** — Re-sending a captured legitimate ciphertext to the server to repeat an action.
- **Sticky session / Session affinity** — Load-balancer routing that pins a connection to a specific backend.
- **Trim safety** — Library code annotated such that the .NET trimmer can analyze references and emit warnings instead of silently producing broken IL at publish time.

---

## Requirements

### Requirement 1: MAUI and NativeAOT compatibility

**User Story:** As a .NET developer publishing a MAUI client app for iOS, MacCatalyst, Android, and Windows, I want the G9SignalRSuperNetCore client and any types it consumes from the server package to publish under full AOT and NativeAOT without trim warnings, so that my app starts fast, ships a smaller binary, and does not crash on iOS due to `Reflection.Emit`.

#### Acceptance Criteria

1. WHEN the consumer publishes a .NET MAUI app referencing `G9SignalRSuperNetCore.Client` for iOS in Release configuration with AOT enabled, THE library SHALL build and run without `PlatformNotSupportedException` from any code path that the consumer can reach in normal use.
2. WHEN the consumer publishes a .NET MAUI app for any of {Android, iOS, MacCatalyst, Windows}, THE library SHALL produce zero AOT analyzer warnings (`IL3050`, `IL3051`, `IL3053`, `IL3054`, `IL3056`) attributable to G9SignalRSuperNetCore code.
3. WHEN the consumer publishes a console or server app with `<PublishAot>true</PublishAot>` and `<TrimMode>full</TrimMode>` referencing G9SignalRSuperNetCore.Client and/or .Server, THE library SHALL produce zero trim warnings (`IL2026`, `IL2046`, `IL2055`, `IL2057`, `IL2060`, `IL2062`, `IL2065`, `IL2067`, `IL2070`, `IL2072`, `IL2075`, `IL2080`, `IL2087`, `IL2090`, `IL2091`, `IL2104`, `IL2111`) attributable to G9SignalRSuperNetCore code.
4. THE library SHALL NOT depend on `Castle.Core` or any other library that uses `Reflection.Emit`, `System.Reflection.Emit.AssemblyBuilder`, or runtime IL generation.
5. THE library SHALL NOT use reflection-based method invocation (`MethodInfo.Invoke`, `Delegate.DynamicInvoke`, `Activator.CreateInstance` of generic types from runtime data) on the per-call hot path of either client-to-server or server-to-client invocations.
6. WHERE a typed client proxy is generated for a hub interface, THE library SHALL produce that proxy at build time via a Roslyn source generator and SHALL NOT generate it at runtime.
7. WHERE listener registration would require reflecting over an interface, THE library SHALL provide a source-generator-emitted registration function so that all `HubConnection.On<...>(...)` bindings are emitted as concrete typed code at compile time.
8. WHEN the consumer enables JSON serialization, THE library SHALL allow a `JsonSerializerContext` (System.Text.Json source-generated metadata) to be supplied, and SHALL NOT require reflection-based metadata.
9. WHEN the consumer enables MessagePack serialization, THE library SHALL allow `MessagePack-CSharp` source-generated formatters and SHALL NOT require runtime formatter generation (`DynamicGenericResolver`, `DynamicObjectResolver`).
10. THE library SHALL declare `<IsAotCompatible>true</IsAotCompatible>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` in every shipped library project file.
11. THE library's continuous integration pipeline SHALL include an AOT publish smoke test (publishing a representative console client with `PublishAot=true`) and SHALL fail the build when that smoke test fails or emits AOT/trim warnings.
12. WHERE a public API depends on a generic type parameter that must be instantiable at runtime, THE method SHALL be annotated with `[DynamicallyAccessedMembers(...)]` describing the required members and SHALL document the AOT requirements in its XML doc comment.

### Requirement 2: Hot-path performance

**User Story:** As an operator running a SignalR server hosting many concurrent hubs and high message rates, I want per-call CPU and allocations to be minimal so that throughput per core is high and GC pressure stays low under load.

#### Acceptance Criteria

1. WHEN a client invokes a server hub method through the typed proxy, THE library SHALL allocate at most: one `Task<T>` (or `ValueTask<T>` where the underlying transport supports it), the parameter object array required by `HubConnection.InvokeCoreAsync`, and any objects intrinsic to the user-supplied arguments. No per-call `MethodInfo` lookup, dictionary lookup, or boxing of value-type return values SHALL occur.
2. WHEN the server invokes a listener method on a connected client, THE library SHALL bind to the underlying `HubConnection.On` callback through a typed delegate (`Func<...>` produced by the source generator) and SHALL NOT invoke through `MethodInfo.Invoke`.
3. WHEN the consumer awaits a `ListenOnceAsync` or `SendThenListenOnceAsync` overload, THE library SHALL return the result through a strongly typed `ValueTuple<...>` without boxing scalar parameters and without a `dynamic` tuple allocation path.
4. THE library SHALL expose the request/response helpers as `ValueTask<T>` overloads in addition to the existing `Task<T>` overloads, so that synchronous-completion paths avoid an additional `Task` allocation.
5. WHERE buffers are required to serialize or encrypt outbound payloads, THE library SHALL acquire them from `ArrayPool<byte>.Shared` (or an equivalent `MemoryPool`) and SHALL release them via a `try/finally` or `using` block before the awaiting caller observes the result.
6. THE library SHALL provide an opt-in MessagePack hub protocol registration helper that wires the official `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` package on both server and client.
7. WHEN the consumer enables MessagePack, THE library SHALL pass the user's `MessagePackSerializerOptions` through unchanged and SHALL NOT inject reflection-based resolvers.
8. THE library SHALL ship a BenchmarkDotNet project that measures, at minimum: cold-start handshake duration, per-call client→server invocation throughput and allocations (1 KB payload), per-call server→client callback throughput and allocations, and JSON-vs-MessagePack throughput. The benchmark project SHALL publish results that the maintainer can compare across versions.
9. WHERE the existing `G9JWTokenFactory` re-creates `JwtSecurityTokenHandler` per call, THE library SHALL reuse a single thread-safe handler instance per process to avoid the documented per-instance setup cost.

### Requirement 3: Horizontal scale-out

**User Story:** As a platform owner expecting between 100 thousand and a few million simultaneous SignalR clients, I want the library to be a transparent thin layer over ASP.NET Core SignalR's first-class scale-out mechanisms, so that I can horizontally scale across server instances without rewriting hubs or losing per-connection features.

#### Acceptance Criteria

1. THE library SHALL NOT store any per-connection state in process-static fields that the rest of the library relies on for correctness.
2. WHERE the existing `G9AHubBaseWithSession*` types use a static `ConcurrentDictionary<Guid, ConcurrentDictionary<string, TSession>>` keyed by hub type, THE library SHALL replace that with an injectable `IG9SessionStore<TSession>` abstraction with the existing behavior available as the default (`G9InMemorySessionStore<TSession>`) and at least one distributed implementation (Redis-backed) shipped alongside it.
3. THE library SHALL provide an extension method `AddG9SignalRBackplane(...)` that registers either a Redis backplane (using `Microsoft.AspNetCore.SignalR.StackExchangeRedis`) or Azure SignalR Service (`Microsoft.Azure.SignalR`) based on configuration, and SHALL document which scenarios each is appropriate for.
4. WHEN a hub is configured for scale-out and a server-to-client message is sent that targets a connection on a different server instance, THE library SHALL deliver that message through the configured backplane without requiring any change to the hub method's source code.
5. THE library SHALL document a tested scaling baseline: target operating system tuning (`ulimit`, `somaxconn`, ephemeral port range on Linux), Kestrel `MaxConcurrentConnections` guidance, sticky-session requirements when not using Azure SignalR Service, and a concrete connection budget per CPU core based on benchmark results.
6. THE library SHALL produce structured `Microsoft.Extensions.Diagnostics.Metrics` counters for: active connections, authorized connections, current handshake count, current encrypted-channel count, current rate-limit rejections per minute, and current backplane queue depth (if applicable). Counter names SHALL be stable across releases.
7. WHEN backpressure on a slow client causes server send buffers to fill, THE library SHALL surface the buffered byte count via a metric and SHALL provide a configurable per-connection write timeout that closes the connection cleanly when exceeded.
8. THE library SHALL ship an example deployment guide for at least one of: AKS+Redis backplane, Azure App Service+Azure SignalR Service. The example SHALL be runnable end-to-end against a free-tier Azure account.
9. WHERE the library exposes per-user state (`Session`, `IsUserConnected`, `CleanupExpiredSessions`), THE corresponding API SHALL document whether it operates per-instance or globally across the cluster, and the distributed session store implementation SHALL implement the global semantics.

### Requirement 4: Thread safety under high concurrency

**User Story:** As a developer building a feature on top of these hub bases, I want connect/disconnect from the same user to be linearizable with respect to the session connection counter and last-activity timestamp, so that I do not see lost updates, negative connection counts, or stale "online" status under concurrent reconnect storms.

#### Acceptance Criteria

1. WHEN multiple connections from the same user identifier connect and disconnect concurrently, THE session store SHALL maintain `ConnectionCounts` such that for every connect there is exactly one corresponding decrement on disconnect, the value is never negative, and the session is removed only when the count transitions to zero through a disconnect.
2. WHEN multiple writers race to update `LastActivityDateTime`, THE session SHALL retain a value no older than the latest activity that any writer observed for that session, and SHALL never expose a torn write (e.g., partially written `DateTime`).
3. THE library SHALL replace the current `internal set` mutation pattern on `G9ASession.ConnectionCounts` and `G9ASession.LastActivityDateTime` with operations that mutate state only through the session store's update API (compare-and-swap or interlocked equivalents), with a property-based test that asserts the linearizability invariant under randomized interleavings.
4. THE library SHALL document, in XML doc comments and in the README, the thread-safety contract of every public type that is intended to be shared across connections (hub bases, session store interface, JWT factory).
5. WHERE the `G9GetJwtHub._validateUserAndGenerateJWTokenPerRoute` static dictionary is populated during startup, THE library SHALL replace the leading-underscore static field with a properly encapsulated registry type and SHALL document that the registry is read-only after startup.
6. THE library's test project SHALL include property-based tests (FsCheck or CsCheck) for: session connect/disconnect linearizability, JWT issue/validate round-trip equivalence, encrypted-channel framing round-trip equivalence, and rate-limiter monotonic semantics.

### Requirement 5: Operator-facing attributes and policies

**User Story:** As a hub author, I want declarative attributes and configuration to control rate limits, connection limits, replay protection, and observability per hub or per method, so that I can enforce production-grade policies without writing infrastructure code.

#### Acceptance Criteria

1. WHEN a hub method is decorated with `[G9AttrRateLimit(perSecond, burst)]` (or equivalent), THE library SHALL reject invocations from a single connection that exceed the configured envelope, return an error to the caller with a stable error code (`G9_RATE_LIMITED`), and SHALL emit a metric and structured log entry per rejection.
2. THE library SHALL provide a configurable per-user maximum concurrent connection count and a per-IP maximum concurrent connection count, both enforced server-side at connect time.
3. THE library SHALL provide replay protection beyond the basic JWT replay flag, applied to encrypted-channel payloads (sequence numbers + sliding window) when the encrypted channel is enabled.
4. THE library SHALL provide a `[G9AttrTelemetry(name)]` attribute that emits `ActivitySource` traces around hub method execution, and an `IG9HubMetrics` interface for the consumer to plug in custom counters.
5. WHERE the existing `[G9AttrDenyAccess]` attribute applies a deny-by-default authorization policy, THE library SHALL preserve that exact behavior and SHALL keep the attribute name, namespace, and constructor signature.
6. WHEN a hub method is decorated with `[G9AttrConnectionRequired]` (new), THE library SHALL reject invocations from connections that are not in the `Connected` state with a clear error rather than letting SignalR throw an opaque exception.
7. WHERE the existing `[G9AttrExcludeFromClientGeneration]` attribute affects build-time client generation, THE library SHALL preserve that attribute exactly and SHALL ensure the new source generator honors it.

### Requirement 6: Application-layer secure channel without TLS (G9-Secure-Channel)

**User Story:** As an integrator deploying SignalR over a network where TLS termination is unavailable or untrusted (legacy intranets, air-gapped staging, embedded devices, jurisdictions blocking specific TLS configurations), I want an opt-in encrypted channel layered on top of SignalR that uses a server static public key the client receives during the first connection, with forward secrecy and replay protection, so that I can deploy a confidential channel without TLS while keeping my SignalR programming model.

#### Acceptance Criteria

1. THE library SHALL implement the secure channel as the **Noise Protocol Framework IK pattern over X25519** for the case where the client has already pinned the server's static public key, and SHALL implement the **Noise XX pattern over X25519** for the bootstrap/first-connect case where the client has not yet pinned a server key.
2. THE library SHALL use only `System.Security.Cryptography` BCL primitives (`ECDiffieHellman`/`X25519`, `HKDF`, `AesGcm`, `ChaCha20Poly1305`) and SHALL NOT implement cryptographic primitives in user code.
3. THE library SHALL negotiate the bulk cipher between AES-256-GCM (preferred where AES-NI / hardware AES is available) and ChaCha20-Poly1305 (preferred otherwise) at handshake time. Negotiation SHALL prefer AES-GCM by default and SHALL be overridable by configuration.
4. WHEN the client connects without a pre-pinned server key, THE library SHALL run the XX handshake to exchange and authenticate static keys, present the server's static public key fingerprint to the consumer through a `IG9ServerKeyVerifier` callback, and SHALL refuse to proceed if the verifier returns false. The consumer is responsible for trust-on-first-use storage and out-of-band verification.
5. WHEN the client connects with a pre-pinned server key, THE library SHALL run the IK handshake against that pinned key and SHALL fail the handshake if the server's static public key does not match the pinned value.
6. THE library SHALL derive a fresh symmetric key for each direction of every connection (client→server and server→client) via HKDF-SHA-256 from the Noise handshake hash, and SHALL rekey at a configurable interval (default: every 2^20 messages or every 60 minutes, whichever comes first).
7. THE library SHALL include a per-direction monotonically increasing 64-bit nonce for every encrypted message, SHALL reject messages whose nonce is not strictly greater than the last accepted nonce on that direction, and SHALL never accept a message with a duplicate nonce on the same key.
8. WHERE a hub is decorated with `[G9AttrRequireSecureChannel]`, THE library SHALL refuse to process any hub method invocation on connections that have not completed the secure-channel handshake, returning a stable error code (`G9_SECURE_CHANNEL_REQUIRED`).
9. THE library SHALL document that the secure channel is **not a multi-party end-to-end protocol**: the server can decrypt all messages it receives. The threat model THE secure channel addresses is "passive eavesdropper or active MITM on the network between client and server when TLS is not in use", explicitly excluding "untrusted server."
10. THE library SHALL document that, without out-of-band public-key authentication (pinning, signing, distribution channel), trust-on-first-use is vulnerable to MITM on the very first connection, and SHALL refuse to enable IK without a pinned key.
11. THE library SHALL ship property-based tests for the secure channel that assert: (a) round-trip plaintext equivalence under any key/nonce schedule, (b) decryption fails with `AuthenticationTagMismatchException` under any single-byte ciphertext mutation, (c) replay of a previously accepted ciphertext is rejected, and (d) nonces never wrap on the same key.
12. WHEN secure channel is active, the per-message overhead SHALL be ≤ 32 bytes (16-byte AEAD tag plus a 16-byte framing/nonce header) and per-message added latency SHALL be ≤ 200 µs at the 95th percentile on a representative desktop CPU for payloads up to 4 KB. These targets SHALL be measured by the benchmark project and reported as part of release notes.
13. THE library SHALL provide a SignalR `IHubProtocol` wrapper or middleware extension point so the secure channel composes cleanly with both the JSON and MessagePack hub protocols without re-implementing them.

### Requirement 7: Backward compatibility and migration path

**User Story:** As an existing consumer of G9SignalRSuperNetCore 1.0.x, I want to upgrade to the hardened 2.0 release without rewriting my hubs and clients in one step, so that I can adopt new features incrementally.

#### Acceptance Criteria

1. THE library SHALL ship the hardening changes as a major version bump (2.0.0) and SHALL document every breaking change in `CHANGELOG.md` with a concrete before/after migration snippet.
2. WHERE a public type is renamed or moved, THE library SHALL keep a `[Obsolete(error: false)]` type alias in the original location for one major version (i.e., 2.x will keep 1.x type aliases; 3.x may remove them).
3. WHEN an existing consumer references the runtime Castle proxy client, THE library SHALL provide a clear `[Obsolete]` warning pointing to the source-generator client and SHALL keep the runtime client compiling but flagged.
4. THE library SHALL ship a one-page migration guide that explains how to: (a) regenerate clients with the new source generator, (b) opt into MessagePack, (c) opt into the secure channel, (d) opt into the distributed session store, and (e) interpret new metrics.
5. WHEN a hub is configured today with an in-memory static session store, THE 2.0 default behavior SHALL be the same in-memory implementation, so that no consumer experiences a behavior change unless they opt into the new abstractions.
6. THE library SHALL include integration tests that exercise the 1.x sample (`G9SignalRSuperNetCore.WebServer` + `G9SignalRSuperNetCore.ConsoleClient`) recompiled against 2.x with only the documented migration snippets applied, and SHALL fail the build if any of those samples fail to run.

---

## Correctness Properties (PBT-friendly)

These properties will be encoded as executable property tests during implementation.

1. **Session linearizability:** For any randomized interleaving of `Connect(userId)` / `Disconnect(userId)` operations, the final `ConnectionCounts` for each user equals (#connects − #disconnects), is never negative at any observable point, and the session is present in the store iff `ConnectionCounts > 0`.
2. **JWT round-trip equivalence:** For any valid `(secret, issuer, audience, claims, lifetime)` tuple, a token produced by `G9JWTokenFactory.GenerateJWTToken(...)` validates against the produced `TokenValidationParameters` and yields the same claim set that was passed in.
3. **Encrypted channel round-trip equivalence:** For any plaintext `m` and any valid handshake transcript, `Decrypt(Encrypt(m, key, nonce_n)) = m` and the inverse holds for both directions.
4. **Encrypted channel tamper detection:** For any ciphertext produced by the channel, mutating any single byte (including the tag, the nonce header, or the payload) causes decryption to fail with an authentication error.
5. **Encrypted channel replay rejection:** For any pair of accepted message nonces `n1 < n2`, replaying a previously accepted message with nonce `n1` after `n2` has been accepted is rejected.
6. **Rate limiter monotonicity:** For any sequence of `Allow()` calls within a fixed window, the number of `true` results is bounded by `burst + floor(elapsed * rate)` and never exceeds the configured `burst`.
7. **Source-generator parity:** For every public hub method that does not carry `[G9AttrExcludeFromClientGeneration]`, the source generator emits exactly one client-side proxy method whose signature is structurally equal to the hub method's signature (parameter list, return type modulo `Task` ↔ `ValueTask` opt-in).

---

## Out of Scope (this iteration)

- Multi-party / device-to-device end-to-end encryption (Signal-style double ratchet). The secure channel is client↔server only.
- Replacing ASP.NET Core SignalR with a custom transport. We compose with SignalR; we do not fork it.
- Compatibility with the legacy ASP.NET (non-Core) SignalR.
- Persistent message replay/durability across reconnects (this is an application concern; we only guarantee at-most-once decrypt of a given nonce and ordered delivery within a connection).
- A managed admin/dashboard UI.
