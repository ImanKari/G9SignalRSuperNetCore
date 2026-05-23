using System.Reflection;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Groups;

/// <summary>
///     Hub-typed filter that adds every newly-connected connection to the groups declared by
///     <see cref="G9AttrAutoJoinGroupAttribute"/> on <typeparamref name="THub"/>.
/// </summary>
/// <typeparam name="THub">The hub type whose connections are auto-joined.</typeparam>
/// <remarks>
///     <para>Registered automatically by
///     <see cref="G9SignalRSuperNetCoreServer.AddG9SignalRSuperNetCoreGroups{THub}"/>. The
///     filter only fires on hubs of the matching type, and does nothing if
///     <typeparamref name="THub"/> carries no <see cref="G9AttrAutoJoinGroupAttribute"/>.</para>
///     <para>AOT-safe: <typeparamref name="THub"/> is fully resolved at compile time and the
///     filter never invokes reflection on a constructed type, so no
///     <c>MakeGenericType</c> is needed.</para>
/// </remarks>
public sealed class G9CAutoJoinFilter<THub> : IHubFilter where THub : Hub
{
    private static readonly G9AttrAutoJoinGroupAttribute[] AutoJoins =
        typeof(THub).GetCustomAttributes<G9AttrAutoJoinGroupAttribute>().ToArray();

    private readonly G9CGroupManager<THub> _groups;

    /// <summary>Initializes the filter.</summary>
    public G9CAutoJoinFilter(G9CGroupManager<THub> groups) => _groups = groups;

    /// <inheritdoc />
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (AutoJoins.Length > 0)
        {
            foreach (var attr in AutoJoins)
                await _groups.JoinAsync(context.Context.ConnectionId, attr.GroupName).ConfigureAwait(false);
        }
        await next(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
        => next(context);

    /// <inheritdoc />
    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
        => next(context, exception);
}
