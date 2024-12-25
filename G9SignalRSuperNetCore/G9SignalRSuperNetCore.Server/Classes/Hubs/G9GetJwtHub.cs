using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

public class G9GetJwtHub : Hub
{
    internal static ConcurrentDictionary<string, Func<object, Hub, Task<G9JWTokenFactory>>>
        _validateUserAndGenerateJWTokenPerRoute = [];

    public async Task Authorize(object authorizeData)
    {
        var routePattern = Context.GetHttpContext()?.Request.Path;
        if (_validateUserAndGenerateJWTokenPerRoute.TryGetValue(routePattern, out var func))
        {
            var result = await func(authorizeData, this);
            if (result.IsRejected)
                await Clients.Caller.SendCoreAsync("AuthorizeResult", [false, result.RejectionReason, null]);
            else
                await Clients.Caller.SendCoreAsync("AuthorizeResult", [true, null, result.JWToken]);
        }
    }
}