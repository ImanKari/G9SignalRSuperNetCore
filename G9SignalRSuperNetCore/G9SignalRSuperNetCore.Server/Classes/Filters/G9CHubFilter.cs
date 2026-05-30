using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
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
///     <see cref="G9AttrRequireClaimAttribute"/>, and <see cref="G9AttrTelemetryAttribute"/>.
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
    private readonly ConcurrentDictionary<MethodInfo, MethodMeta> _methodCache = new();
    private readonly ConcurrentDictionary<string, G9CTokenBucket> _buckets = new(StringComparer.Ordinal);
    private readonly G9CConnectionCounter _userConnections = new();
    private readonly G9CConnectionCounter _ipConnections = new();
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes the filter. The <paramref name="logger"/> is resolved from DI when the
    ///     filter is registered through <c>AddSignalRSuperNetCoreCore()</c>; it is used to emit a
    ///     structured warning whenever a policy (rate limit, connection limit, role/claim,
    ///     connection-required) rejects an invocation, so operators can see <i>why</i> a call was
    ///     refused instead of only observing the client-side <see cref="HubException"/>. The
    ///     policy metrics on <see cref="G9CTelemetry"/> are still incremented regardless of the
    ///     logger.
    /// </summary>
    public G9CHubFilter(ILogger<G9CHubFilter> logger)
    {
        _logger = logger ?? NullLogger<G9CHubFilter>.Instance;
    }

    /// <summary>
    ///     Parameterless fallback used when the filter is constructed outside DI (e.g. in unit
    ///     tests or a manual <c>new G9CHubFilter()</c>). Policy logging is suppressed in this mode
    ///     (routed to <see cref="NullLogger"/>); metrics still fire.
    /// </summary>
    public G9CHubFilter() : this(NullLogger<G9CHubFilter>.Instance)
    {
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
        if (meta.RequiredRoles is not null)
        {
            if (user is null || !meta.RequiredRoles.Any(user.IsInRole))
            {
                G9CTelemetry.AuthorizationRejections.Add(1);
                LogPolicyRejection(_logger, G9CErrorCodes.RoleRequired, context.HubMethodName,
                    context.Context.ConnectionId, context.Context.UserIdentifier,
                    "requires role: " + string.Join(",", meta.RequiredRoles));
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

        // Rate limit (per connection + method).
        if (meta.RateLimit is not null)
        {
            var key = string.Concat(context.Context.ConnectionId, "|", context.HubMethodName);
            var bucket = _buckets.GetOrAdd(key, _ => new G9CTokenBucket(meta.RateLimit.PerSecond, meta.RateLimit.Burst));
            if (!bucket.TryAcquire())
            {
                G9CTelemetry.RateLimitedInvocations.Add(1);
                LogPolicyRejection(_logger, G9CErrorCodes.RateLimited, context.HubMethodName,
                    context.Context.ConnectionId, context.Context.UserIdentifier,
                    $"limit {meta.RateLimit.PerSecond}/s burst {meta.RateLimit.Burst}");
                throw new HubException(G9CErrorCodes.RateLimited);
            }
        }

        // Optional telemetry span.
        if (meta.TelemetryName is not null)
        {
            using var activity = G9CTelemetry.ActivitySource.StartActivity(meta.TelemetryName, ActivityKind.Server);
            activity?.SetTag("g9.connection_id", context.Context.ConnectionId);
            activity?.SetTag("g9.user_id", context.Context.UserIdentifier);
            try
            {
                var result = await next(context).ConfigureAwait(false);
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
            var userId = context.Context.UserIdentifier;
            var ip = context.Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

            if (limit.PerUser > 0 && !string.IsNullOrEmpty(userId)
                && !_userConnections.TryIncrement(userId, limit.PerUser))
            {
                G9CTelemetry.ConnectionLimitRejections.Add(1);
                LogConnectionRejection(_logger, hubType.Name, "per-user", userId, limit.PerUser, null);
                throw new HubException(G9CErrorCodes.ConnectionLimit);
            }

            if (limit.PerIp > 0 && !string.IsNullOrEmpty(ip)
                && !_ipConnections.TryIncrement(ip, limit.PerIp))
            {
                if (limit.PerUser > 0 && !string.IsNullOrEmpty(userId)) _userConnections.Decrement(userId);
                G9CTelemetry.ConnectionLimitRejections.Add(1);
                LogConnectionRejection(_logger, hubType.Name, "per-ip", ip, limit.PerIp, null);
                throw new HubException(G9CErrorCodes.ConnectionLimit);
            }
        }

        // Bundle 3: presence tracking (opt-in via [G9AttrPresenceTracked]).
        if (hubType.GetCustomAttribute<G9AttrPresenceTrackedAttribute>() is not null)
        {
            var tracker = context.Context.GetHttpContext()?.RequestServices.GetService<G9CPresenceTracker>();
            var key = context.Context.UserIdentifier ?? context.Context.ConnectionId;
            tracker?.OnConnected(key);
        }

        // NOTE: Bundle 3 auto-join (G9AttrAutoJoinGroup) is handled by the hub-typed
        // G9CAutoJoinFilter<THub> registered via AddG9SignalRSuperNetCoreGroups<THub>(). We don't
        // join here because doing so would require MakeGenericType, which isn't AOT-safe.

        await next(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        var hubType = context.Hub.GetType();
        var limit = hubType.GetCustomAttribute<G9AttrConnectionLimitAttribute>();
        if (limit is not null)
        {
            var userId = context.Context.UserIdentifier;
            var ip = context.Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
            if (limit.PerUser > 0 && !string.IsNullOrEmpty(userId)) _userConnections.Decrement(userId);
            if (limit.PerIp > 0 && !string.IsNullOrEmpty(ip)) _ipConnections.Decrement(ip);
        }

        // Bundle 3: presence tracking (opt-in).
        if (hubType.GetCustomAttribute<G9AttrPresenceTrackedAttribute>() is not null)
        {
            var tracker = context.Context.GetHttpContext()?.RequestServices.GetService<G9CPresenceTracker>();
            var key = context.Context.UserIdentifier ?? context.Context.ConnectionId;
            tracker?.OnDisconnected(key);
        }

        // Bundle 5: drop the cached session key (if any) so its bytes are zeroed.
        var sealer = context.Context.GetHttpContext()?.RequestServices.GetService<G9CSessionSealer>();
        sealer?.DropSession(context.Context.ConnectionId);

        await next(context, exception).ConfigureAwait(false);
    }

    private MethodMeta GetOrAddMeta(MethodInfo method) =>
        _methodCache.GetOrAdd(method, static m =>
        {
            var rate = m.GetCustomAttribute<G9AttrRateLimitAttribute>();
            var requireConn = m.GetCustomAttribute<G9AttrConnectionRequiredAttribute>() is not null;
            var roles = m.GetCustomAttributes<G9AttrRequireRoleAttribute>()
                .SelectMany(a => a.Roles).Distinct(StringComparer.Ordinal).ToArray();
            var claims = m.GetCustomAttributes<G9AttrRequireClaimAttribute>()
                .Select(a => (a.ClaimType, a.AcceptedValues)).ToArray();
            var telemetry = m.GetCustomAttribute<G9AttrTelemetryAttribute>();
            string? telemetryName = telemetry is null ? null : (telemetry.Name ?? $"{m.DeclaringType?.Name}.{m.Name}");

            return new MethodMeta(
                rate, requireConn,
                roles.Length > 0 ? roles : null,
                claims.Length > 0 ? claims : null,
                telemetryName);
        });

    private sealed record MethodMeta(
        G9AttrRateLimitAttribute? RateLimit,
        bool RequireConnection,
        string[]? RequiredRoles,
        (string Type, string[] Accepted)[]? RequiredClaims,
        string? TelemetryName);

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
}
