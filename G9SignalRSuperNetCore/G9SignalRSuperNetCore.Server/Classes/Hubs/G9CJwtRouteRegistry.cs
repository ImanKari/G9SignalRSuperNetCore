using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

/// <summary>
///     A process-wide registry that maps a JWT-authentication SignalR route pattern
///     (for example <c>/AuthHub</c>) to the delegate that validates user credentials
///     and produces a JWT for that route.
/// </summary>
/// <remarks>
///     <para>
///         Populated during application startup by
///         <c>AddSignalRSuperNetCoreServerHub&lt;TTargetClass, TClientSideMethodsInterface&gt;</c>.
///         Read on every authorize request by <see cref="G9GetJwtHub"/>.
///     </para>
///     <para>
///         The internal storage is a <see cref="ConcurrentDictionary{TKey,TValue}"/> so reads
///         are lock-free and concurrent. Registrations are expected to occur once at startup;
///         subsequent registrations for the same route are ignored.
///     </para>
/// </remarks>
internal static class G9CJwtRouteRegistry
{
    private static readonly ConcurrentDictionary<string, Func<object, Hub, Task<(G9JWTokenFactory, object?)>>>
        Handlers = new(StringComparer.Ordinal);

    /// <summary>
    ///     Registers (idempotently) the authentication handler for a route pattern.
    /// </summary>
    public static void Register(string routePattern, Func<object, Hub, Task<(G9JWTokenFactory, object?)>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(routePattern);
        ArgumentNullException.ThrowIfNull(handler);
        Handlers.TryAdd(routePattern, handler);
    }

    /// <summary>
    ///     Tries to resolve the handler for the given route pattern.
    /// </summary>
    public static bool TryGet(string routePattern, out Func<object, Hub, Task<(G9JWTokenFactory, object?)>>? handler)
        => Handlers.TryGetValue(routePattern, out handler);
}
