using System.Collections.Concurrent;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;

namespace G9SignalRSuperNetCore.Tests.Infrastructure;

/// <summary>
///     What the transport tests need from the server side of the socket: every HTTP request it received (so a test can
///     say "no negotiate request was made"), and a way to cut the TCP connections under a live SignalR connection
///     without either side closing it (what a network break looks like).
/// </summary>
public sealed class ServerProbe
{
    private readonly ConcurrentDictionary<string, ConnectionContext> _live = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _requests = new();

    /// <summary>Requests in arrival order, as <c>"POST /test/negotiate"</c>.</summary>
    public IReadOnlyCollection<string> Requests => _requests;

    public int Count(string method, string path) =>
        _requests.Count(r => string.Equals(r, method + " " + path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Aborts every open TCP connection. Returns how many there were.</summary>
    public int CutConnections()
    {
        var cut = 0;
        foreach (var connection in _live.Values)
        {
            connection.Abort();
            cut++;
        }

        return cut;
    }

    internal Func<ConnectionDelegate, ConnectionDelegate> ConnectionMiddleware => next => async context =>
    {
        _live[context.ConnectionId] = context;
        try
        {
            await next(context);
        }
        finally
        {
            _live.TryRemove(context.ConnectionId, out _);
        }
    };

    internal Func<HttpContext, RequestDelegate, Task> RequestMiddleware => (context, next) =>
    {
        _requests.Enqueue(context.Request.Method + " " + context.Request.Path);
        return next(context);
    };
}
