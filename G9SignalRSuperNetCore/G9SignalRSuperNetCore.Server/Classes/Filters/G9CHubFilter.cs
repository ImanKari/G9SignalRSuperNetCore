using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Channels;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Authorization;
using G9SignalRSuperNetCore.Server.Classes.Connections;
using G9SignalRSuperNetCore.Server.Classes.Crypto;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.Presence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     Process-wide hub filter that enforces the G9 attribute set on every hub invocation:
///     <see cref="G9AttrConnectionLimitAttribute"/>, <see cref="G9AttrConnectionRequiredAttribute"/>,
///     <see cref="G9AttrRateLimitAttribute"/>, <see cref="G9AttrRequireRoleAttribute"/>,
///     <see cref="G9AttrRequireClaimAttribute"/>, <see cref="G9AttrRequirePermissionAttribute"/> (2.9), and
///     <see cref="G9AttrTelemetryAttribute"/>. It also keeps the optional <see cref="IG9UserConnectionIndex"/> current (2.9).
/// </summary>
/// <remarks>
///     <para>The filter is registered as a singleton through the SignalR options pipeline by
///     <c>AddSignalRSuperNetCoreCore()</c>. It maintains <i>process-local</i> state for rate
///     buckets and connection counters; cluster-wide enforcement is a Bundle 5 concern.</para>
///     <para>Performance contract: the filter takes the fast path through a per-method
///     <see cref="MethodMeta"/> cache and never invokes reflection on the hot path. Methods that
///     carry no G9 attributes pay the cost of one dictionary lookup and one
///     <see cref="ConcurrentDictionary{TKey, TValue}"/> miss-then-insert (only on the first call
///     of that method).</para>
/// </remarks>
public sealed partial class G9CHubFilter : IHubFilter
{
    private const int IdleSweepThreshold = 10_000;
    private const string UserKeyPrefix = "u:";
    private const string IpKeyPrefix = "ip:";
    private static readonly TimeSpan IdleSweepAfter = TimeSpan.FromHours(1);

    /// <summary>Key under <see cref="HubCallerContext.Items"/> for the shared rate-limit keys a connection holds.</summary>
    private static readonly object ScopeKeysItem = new();

    /// <summary>The connection-limit counters this connection holds (released exactly once, never re-derived later).</summary>
    private static readonly object LimitKeysItem = new();

    private readonly ConcurrentDictionary<MethodInfo, MethodMeta> _methodCache = new();
    // Keyed by connection FIRST so a disconnect drops that connection's buckets in one O(1) removal
    // instead of scanning every bucket in the process (Bundle 5 review, T02).
    private readonly ConcurrentDictionary<string, ConnectionBuckets> _buckets = new(StringComparer.Ordinal);
    private readonly G9CConnectionCounter _userConnections = new();
    private readonly G9CConnectionCounter _ipConnections = new();
    // Live connections per user / per IP (2.9), so a user- or IP-scoped bucket is freed with the LAST connection sharing it.
    private readonly G9CKeyRefCounter _scopeKeys = new();
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes the filter.
    ///     <para>
    ///         The optional <paramref name="logger"/> is resolved from DI when the filter is
    ///         registered through <c>AddSignalRSuperNetCoreCore()</c> (logging is always present in
    ///         an ASP.NET Core host); it is used to emit a structured warning whenever a policy
    ///         (rate limit, connection limit, role/claim, connection-required) rejects an
    ///         invocation, so operators can see <i>why</i> a call was refused instead of only
    ///         observing the client-side <see cref="HubException"/>. The policy metrics on
    ///         <see cref="G9CTelemetry"/> are still incremented regardless of the logger.
    ///     </para>
    ///     <para>
    ///         There is exactly ONE constructor on purpose. SignalR registers hub filters through
    ///         <c>ActivatorUtilities.CreateFactory</c> (see <c>HubOptions.AddFilter&lt;T&gt;()</c>),
    ///         which throws <c>InvalidOperationException("Multiple constructors accepting all given
    ///         argument types…")</c> when a filter type exposes more than one DI-satisfiable
    ///         constructor. A single constructor with an optional argument keeps DI resolution,
    ///         <c>AddFilter&lt;G9CHubFilter&gt;()</c>, and a manual <c>new G9CHubFilter()</c> (unit
    ///         tests) all working.
    ///     </para>
    /// </summary>
    public G9CHubFilter(ILogger<G9CHubFilter>? logger = null)
    {
        _logger = logger ?? NullLogger<G9CHubFilter>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var meta = GetOrAddMeta(context.HubMethod);

        // Connection state guard (fast: HubCallerContext exposes the abort token; if the connection
        // is aborted we fail fast with a clear code).
        if (meta.RequireConnection && context.Context.ConnectionAborted.IsCancellationRequested)
        {
            LogPolicyRejection(_logger, G9CErrorCodes.ConnectionRequired, context.HubMethodName,
                context.Context.ConnectionId, context.Context.UserIdentifier, null);
            throw new HubException(G9CErrorCodes.ConnectionRequired);
        }

        // Authorization guards (role / claim).
        var user = context.Context.User;
        if (meta.RequiredRoleSets is not null)
        {
            // Each set is "any one of these roles"; every set (class level, method level) must pass.
            foreach (var roles in meta.RequiredRoleSets)
            {
                if (user is not null && roles.Any(user.IsInRole)) continue;
                G9CTelemetry.AuthorizationRejections.Add(1);
                LogPolicyRejection(_logger, G9CErrorCodes.RoleRequired, context.HubMethodName,
                    context.Context.ConnectionId, context.Context.UserIdentifier,
                    "requires role: " + string.Join(",", roles));
                throw new HubException(G9CErrorCodes.RoleRequired);
            }
        }

        if (meta.RequiredClaims is not null)
        {
            foreach (var (type, accepted) in meta.RequiredClaims)
            {
                if (user is null)
                {
                    G9CTelemetry.AuthorizationRejections.Add(1);
                    LogPolicyRejection(_logger, G9CErrorCodes.ClaimRequired, context.HubMethodName,
                        context.Context.ConnectionId, context.Context.UserIdentifier, "claim: " + type);
                    throw new HubException(G9CErrorCodes.ClaimRequired);
                }
                var ok = accepted.Length == 0
                    ? user.HasClaim(c => c.Type == type)
                    : user.HasClaim(c => c.Type == type && Array.IndexOf(accepted, c.Value) >= 0);
                if (!ok)
                {
                    G9CTelemetry.AuthorizationRejections.Add(1);
                    LogPolicyRejection(_logger, G9CErrorCodes.ClaimRequired, context.HubMethodName,
                        context.Context.ConnectionId, context.Context.UserIdentifier, "claim: " + type);
                    throw new HubException(G9CErrorCodes.ClaimRequired);
                }
            }
        }

        // Application-defined permissions (2.9): method-level and class-level, all must be granted.
        if (meta.RequiredPermissions is not null)
        {
            var handler = context.ServiceProvider.GetService<IG9HubPermissionHandler>();
            if (handler is null)
            {
                // Fail closed: a permission that nobody can grant is not granted.
                G9CTelemetry.AuthorizationRejections.Add(1);
                LogPermissionHandlerMissing(_logger, context.HubMethodName,
                    string.Join(",", meta.RequiredPermissions), context.Context.ConnectionId, null);
                throw new HubException(G9CErrorCodes.PermissionRequired);
            }

            foreach (var permission in meta.RequiredPermissions)
            {
                if (await handler.IsAllowedAsync(context, permission).ConfigureAwait(false)) continue;

                G9CTelemetry.AuthorizationRejections.Add(1);
                LogPolicyRejection(_logger, G9CErrorCodes.PermissionRequired, context.HubMethodName,
                    context.Context.ConnectionId, context.Context.UserIdentifier, "permission: " + permission);
                throw new HubException(G9CErrorCodes.PermissionRequired);
            }
        }

        // Rate limit (per method, and per connection / user / IP depending on the scope).
        if (meta.RateLimit is not null)
        {
            // Disconnect is what normally frees these; the sweep only covers a connection whose
            // disconnect never ran, and only once there are more than a healthy process would hold.
            if (_buckets.Count > IdleSweepThreshold) SweepIdleBuckets(IdleSweepAfter);

            var bucketOwner = BucketKey(meta.RateLimit.Scope, context.Context);
            var perOwner = _buckets.GetOrAdd(bucketOwner, static _ => new ConnectionBuckets());
            perOwner.Touch();
            var bucket = perOwner.Methods.GetOrAdd(context.HubMethodName,
                _ => new G9CTokenBucket(meta.RateLimit.PerSecond, meta.RateLimit.Burst));
            if (!bucket.TryAcquire())
            {
                G9CTelemetry.RateLimitedInvocations.Add(1);
                LogPolicyRejection(_logger, G9CErrorCodes.RateLimited, context.HubMethodName,
                    context.Context.ConnectionId, context.Context.UserIdentifier,
                    $"limit {meta.RateLimit.PerSecond}/s burst {meta.RateLimit.Burst} scope {meta.RateLimit.Scope}");
                throw new HubException(G9CErrorCodes.RateLimited);
            }
        }

        // Optional telemetry span (2.9: sampled by G9AttrTelemetry.SampleRate).
        if (meta.TelemetryName is not null && IsSampled(meta.TelemetrySampleRate))
        {
            var activity = G9CTelemetry.ActivitySource.StartActivity(meta.TelemetryName, ActivityKind.Server);
            activity?.SetTag("g9.connection_id", context.Context.ConnectionId);
            activity?.SetTag("g9.user_id", context.Context.UserIdentifier);
            try
            {
                var result = await next(context).ConfigureAwait(false);

                // A streaming method returns as soon as its iterator exists, usually before a single item
                // has been produced, so this span covers the invocation and NOT the enumeration. Saying
                // so is the honest thing the filter can do: wrapping the stream would need the item type
                // at runtime, and building it with MakeGenericType is not AOT-safe (T04). A hub that
                // wants enumeration metrics wraps its own stream with G9CStreamTelemetry.Track<T>, where
                // the type is known at compile time. Whether the method streams is read once, from its DECLARED
                // return type (MethodMeta.IsStreaming), so no reflection over the runtime type is needed (IL2070).
                if (meta.IsStreaming)
                {
                    activity?.SetTag("g9.stream", true);
                    activity?.SetTag("g9.outcome", "stream_started");
                    return result;
                }

                activity?.SetTag("g9.outcome", "ok");
                return result;
            }
            catch (OperationCanceledException)
            {
                activity?.SetTag("g9.outcome", "cancelled");
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetTag("g9.outcome", "faulted");
                activity?.SetTag("exception.type", ex.GetType().FullName);
                throw;
            }
            finally
            {
                activity?.Dispose();
            }
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var hubType = context.Hub.GetType();
        var limit = hubType.GetCustomAttribute<G9AttrConnectionLimitAttribute>();
        if (limit is not null)
        {
            // Counted per hub type: every hub declares its own limits, and connections to one hub (plus those still in a
            // reconnect window) must not use up another hub's slots.
            var hubKey = hubType.FullName ?? hubType.Name;
            var rawUser = context.Context.UserIdentifier;
            var rawIp = RemoteIp(context.Context);
            var userId = string.IsNullOrEmpty(rawUser) ? null : hubKey + "|" + rawUser;
            var ip = string.IsNullOrEmpty(rawIp) ? null : hubKey + "|" + rawIp;
            var countedUser = limit.PerUser > 0 && userId is not null;
            var countedIp = limit.PerIp > 0 && ip is not null;

            if (countedUser && !_userConnections.TryIncrement(userId!, limit.PerUser))
            {
                G9CTelemetry.ConnectionLimitRejections.Add(1);
                LogConnectionRejection(_logger, hubType.Name, "per-user", rawUser!, limit.PerUser, null);
                throw new HubException(G9CErrorCodes.ConnectionLimit);
            }

            if (countedIp && !_ipConnections.TryIncrement(ip!, limit.PerIp))
            {
                if (countedUser) _userConnections.Decrement(userId!);
                G9CTelemetry.ConnectionLimitRejections.Add(1);
                LogConnectionRejection(_logger, hubType.Name, "per-ip", rawIp!, limit.PerIp, null);
                throw new HubException(G9CErrorCodes.ConnectionLimit);
            }

            // Remember what was counted: on disconnect the HTTP context may already be disposed, and re-deriving the
            // IP there failed (ObjectDisposedException) and leaked the counters until the address was locked out.
            context.Context.Items[LimitKeysItem] = new LimitKeys(countedUser ? userId : null, countedIp ? ip : null);
        }

        // Bundle 3: presence tracking (opt-in via [G9AttrPresenceTracked]).
        if (hubType.GetCustomAttribute<G9AttrPresenceTrackedAttribute>() is not null)
        {
            var tracker = context.ServiceProvider.GetService<G9CPresenceTracker>();
            var key = context.Context.UserIdentifier ?? context.Context.ConnectionId;
            tracker?.OnConnected(key);
        }

        // NOTE: Bundle 3 auto-join (G9AttrAutoJoinGroup) is handled by the hub-typed
        // G9CAutoJoinFilter<THub> registered via AddG9SignalRSuperNetCoreGroups<THub>(). We don't
        // join here because doing so would require MakeGenericType, which isn't AOT-safe.

        // 2.9: count this connection towards its user / IP rate-limit scopes, and index it by user. Both happen BEFORE
        // the hub's own OnConnectedAsync, so the hub already sees itself in the index.
        var caller = context.Context;
        var scopeKeys = new ScopeKeys(
            string.IsNullOrEmpty(caller.UserIdentifier) ? null : UserKeyPrefix + caller.UserIdentifier,
            RemoteIp(caller) is { } remoteIp ? IpKeyPrefix + remoteIp : null);
        if (scopeKeys.User is not null) _scopeKeys.Acquire(scopeKeys.User);
        if (scopeKeys.Ip is not null) _scopeKeys.Acquire(scopeKeys.Ip);
        caller.Items[ScopeKeysItem] = scopeKeys;
        var index = context.ServiceProvider.GetService<G9CUserConnectionIndex>();
        index?.Add(caller);

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch
        {
            // The hub refused the connection. SignalR does not run OnDisconnectedAsync for a connection whose
            // OnConnectedAsync threw, so what was registered above would otherwise never be released.
            ReleaseScopeKey(scopeKeys.User);
            ReleaseScopeKey(scopeKeys.Ip);
            ReleaseConnectionLimits(caller);
            caller.Items.Remove(ScopeKeysItem);
            index?.Remove(caller.ConnectionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        var hubType = context.Hub.GetType();
        try
        {
            // Never touch the HTTP context here: after an abrupt close its features are disposed.
            ReleaseConnectionLimits(context.Context);

            // Bundle 3: presence tracking (opt-in).
            if (hubType.GetCustomAttribute<G9AttrPresenceTrackedAttribute>() is not null)
            {
                var tracker = context.ServiceProvider.GetService<G9CPresenceTracker>();
                var key = context.Context.UserIdentifier ?? context.Context.ConnectionId;
                tracker?.OnDisconnected(key);
            }

            // Rate-limit buckets are per connection and die with it. Without this the dictionary grew for
            // the life of the process, one entry per historical connection that ever hit a limited method —
            // which on mobile, where every tunnel change is a new connection, is a slow leak (T02).
            _buckets.TryRemove(context.Context.ConnectionId, out _);

            // 2.9: a user- or IP-scoped bucket is shared by every connection of that user / address and dies with the
            // LAST of them; the connection also leaves the user index (before the hub's own OnDisconnectedAsync runs).
            if (context.Context.Items.TryGetValue(ScopeKeysItem, out var held) && held is ScopeKeys keys)
            {
                ReleaseScopeKey(keys.User);
                ReleaseScopeKey(keys.Ip);
            }

            context.ServiceProvider.GetService<G9CUserConnectionIndex>()?.Remove(context.Context.ConnectionId);

            // Bundle 5: drop the cached session key (if any) so its bytes are zeroed.
            var sealer = context.ServiceProvider.GetService<G9CSessionSealer>();
            sealer?.DropSession(context.Context.ConnectionId);
        }
        catch (Exception ex)
        {
            // Library bookkeeping must never skip the hub's own OnDisconnectedAsync (the application's cleanup).
            _logger.LogWarning(ex, "G9 disconnect bookkeeping failed for {ConnectionId}", context.Context.ConnectionId);
        }

        await next(context, exception).ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether a hub method streams to the caller, decided from its DECLARED return type: <c>IAsyncEnumerable&lt;T&gt;</c>
    ///     or <c>ChannelReader&lt;T&gt;</c>, bare or wrapped in <c>Task&lt;&gt;</c> / <c>ValueTask&lt;&gt;</c>. Only
    ///     generic-definition checks are used, which the trimmer needs no annotations for (2.9, replaces a
    ///     <c>GetInterfaces()</c> call on the runtime type that raised IL2070).
    /// </summary>
    private static bool IsStreamingReturnType(Type returnType)
    {
        var type = returnType;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Task<>) || definition == typeof(ValueTask<>)) type = type.GetGenericArguments()[0];
        }

        if (!type.IsGenericType) return false;
        var streamDefinition = type.GetGenericTypeDefinition();
        return streamDefinition == typeof(IAsyncEnumerable<>) || streamDefinition == typeof(ChannelReader<>);
    }

    /// <summary>Who owns the bucket for a call: the connection, <c>"u:" + user</c>, or <c>"ip:" + address</c>.</summary>
    private static string BucketKey(G9ERateLimitScope scope, HubCallerContext caller)
    {
        switch (scope)
        {
            case G9ERateLimitScope.User:
                var user = caller.UserIdentifier;
                return string.IsNullOrEmpty(user) ? caller.ConnectionId : UserKeyPrefix + user;
            case G9ERateLimitScope.Ip:
                return RemoteIp(caller) is { } ip ? IpKeyPrefix + ip : caller.ConnectionId;
            default:
                return caller.ConnectionId;
        }
    }

    private static string? RemoteIp(HubCallerContext caller)
    {
        try
        {
            return caller.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        }
        catch (ObjectDisposedException)
        {
            return null; // the transport is already gone
        }
    }

    /// <summary>Gives back the connection-limit counters recorded at connect (idempotent: the record is removed).</summary>
    private void ReleaseConnectionLimits(HubCallerContext caller)
    {
        if (!caller.Items.TryGetValue(LimitKeysItem, out var held) || held is not LimitKeys keys) return;
        caller.Items.Remove(LimitKeysItem);
        if (keys.User is not null) _userConnections.Decrement(keys.User);
        if (keys.Ip is not null) _ipConnections.Decrement(keys.Ip);
    }

    /// <summary>Uniform per-invocation sampling decision for a rate already clamped to [0, 1].</summary>
    private static bool IsSampled(double rate) => rate >= 1.0 || (rate > 0.0 && Random.Shared.NextDouble() < rate);

    /// <summary>Drops one live connection from a shared scope; the last one out frees the shared bucket.</summary>
    private void ReleaseScopeKey(string? key)
    {
        if (key is not null && _scopeKeys.Release(key)) _buckets.TryRemove(key, out _);
    }

    /// <summary>
    ///     Owners currently holding rate-limit buckets: live connections that called a connection-scoped limited
    ///     method, plus users and IP addresses with a live connection that called a user- or IP-scoped one (2.9).
    ///     It should sit at or below the active connection count; a number that climbs with total connections ever
    ///     made would mean cleanup stopped working.
    /// </summary>
    public int TrackedRateLimitConnections => _buckets.Count;

    /// <summary>
    ///     Fallback for buckets whose disconnect never arrived. Removes connections idle longer than
    ///     <paramref name="idleFor"/>, and is only consulted once the dictionary is larger than a
    ///     healthy process should need, so the normal path stays free of sweeping.
    /// </summary>
    private void SweepIdleBuckets(TimeSpan idleFor)
    {
        var cutoff = DateTime.UtcNow - idleFor;
        foreach (var pair in _buckets)
        {
            if (pair.Value.LastUsedUtc < cutoff) _buckets.TryRemove(pair.Key, out _);
        }
    }

    private MethodMeta GetOrAddMeta(MethodInfo method) =>
        _methodCache.GetOrAdd(method, static m =>
        {
            var rate = m.GetCustomAttribute<G9AttrRateLimitAttribute>();
            var requireConn = m.GetCustomAttribute<G9AttrConnectionRequiredAttribute>() is not null;
            // Policy attributes apply from the method AND from its hub class (and the class's bases): an attribute on the
            // class used to be read for permissions only, so a class-level role or claim check silently protected nothing.
            var hubType = m.ReflectedType ?? m.DeclaringType;
            // Roles: stacked roles on one member mean "any one of them"; a class-level set is a separate gate that must
            // pass too (like [Authorize] on a controller and its action), so a broader method role never widens the class.
            var methodRoles = m.GetCustomAttributes<G9AttrRequireRoleAttribute>()
                .SelectMany(a => a.Roles).Distinct(StringComparer.Ordinal).ToArray();
            var classRoles = (hubType?.GetCustomAttributes<G9AttrRequireRoleAttribute>(inherit: true) ?? [])
                .SelectMany(a => a.Roles).Distinct(StringComparer.Ordinal).ToArray();
            var roleSets = new[] { classRoles, methodRoles }.Where(set => set.Length > 0).ToArray();
            // Claims: every attribute must hold, wherever it is declared.
            var claims = m.GetCustomAttributes<G9AttrRequireClaimAttribute>()
                .Concat(hubType?.GetCustomAttributes<G9AttrRequireClaimAttribute>(inherit: true) ?? [])
                .Select(a => (a.ClaimType, a.AcceptedValues)).ToArray();
            // 2.9: permissions from the method and from its hub class (and the class's bases); all must be granted.
            var permissions = m.GetCustomAttributes<G9AttrRequirePermissionAttribute>()
                .Concat(hubType is null
                    ? Enumerable.Empty<G9AttrRequirePermissionAttribute>()
                    : hubType.GetCustomAttributes<G9AttrRequirePermissionAttribute>(inherit: true))
                .Select(a => a.Permission).Distinct(StringComparer.Ordinal).ToArray();
            // Telemetry: the method's attribute wins; a class-level one traces every method (its Name becomes a prefix).
            var methodTelemetry = m.GetCustomAttribute<G9AttrTelemetryAttribute>();
            var telemetry = methodTelemetry ?? hubType?.GetCustomAttribute<G9AttrTelemetryAttribute>(inherit: true);
            string? telemetryName = telemetry is null ? null
                : methodTelemetry is not null ? (telemetry.Name ?? $"{m.DeclaringType?.Name}.{m.Name}")
                : $"{telemetry.Name ?? hubType?.Name}.{m.Name}";
            var sampleRate = telemetry?.SampleRate ?? 1.0;
            sampleRate = double.IsNaN(sampleRate) ? 1.0 : Math.Clamp(sampleRate, 0.0, 1.0);

            return new MethodMeta(
                rate, requireConn,
                roleSets.Length > 0 ? roleSets : null,
                claims.Length > 0 ? claims : null,
                permissions.Length > 0 ? permissions : null,
                telemetryName,
                sampleRate,
                IsStreamingReturnType(m.ReturnType));
        });

    /// <summary>One connection's rate-limit buckets, by method, with the last time it used any of them.</summary>
    private sealed class ConnectionBuckets
    {
        private long _lastUsedTicks = DateTime.UtcNow.Ticks;

        public ConcurrentDictionary<string, G9CTokenBucket> Methods { get; } = new(StringComparer.Ordinal);

        public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

        public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
    }

    private sealed record MethodMeta(
        G9AttrRateLimitAttribute? RateLimit,
        bool RequireConnection,
        string[][]? RequiredRoleSets,
        (string Type, string[] Accepted)[]? RequiredClaims,
        string[]? RequiredPermissions,
        string? TelemetryName,
        double TelemetrySampleRate,
        bool IsStreaming);

    /// <summary>The user / IP rate-limit scope keys a connection was counted under when it connected.</summary>
    private sealed record ScopeKeys(string? User, string? Ip);

    private sealed record LimitKeys(string? User, string? Ip);

    [LoggerMessage(
        EventId = 9100,
        Level = LogLevel.Warning,
        Message = "G9 policy rejected hub invocation: code={ErrorCode} method={Method} connectionId={ConnectionId} userId={UserId} detail={Detail}")]
    private static partial void LogPolicyRejection(
        ILogger logger, string errorCode, string method, string connectionId, string? userId, string? detail);

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Warning,
        Message = "G9 connection-limit rejected connect: hub={Hub} dimension={Dimension} key={Key} limit={Limit}")]
    private static partial void LogConnectionRejection(
        ILogger logger, string hub, string dimension, string key, int limit, Exception? exception);

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Warning,
        Message = "G9 permission check refused hub invocation because no IG9HubPermissionHandler is registered: method={Method} permissions={Permissions} connectionId={ConnectionId}")]
    private static partial void LogPermissionHandlerMissing(
        ILogger logger, string method, string permissions, string connectionId, Exception? exception);
}
