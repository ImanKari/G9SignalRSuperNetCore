using G9SignalRSuperNetCore.Server.Classes.DataTypes;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

/// <summary>
///     A SignalR Hub that handles JWT authorization requests and token issuance for
///     hubs derived from
///     <see cref="Abstracts.G9AHubBaseWithJWTAuth{TTargetClass,TClientSideMethodsInterface}"/>.
/// </summary>
public class G9GetJwtHub : Hub
{
    /// <summary>
    ///     Authorizes the caller using the route-specific delegate registered at startup
    ///     and returns the resulting <see cref="G9DtAuthorizeResult"/> through the
    ///     <c>AuthorizeResult</c> client method.
    /// </summary>
    /// <param name="authorizeData">
    ///     The authorization payload sent by the client. Forwarded verbatim to the registered
    ///     handler. The shape of this payload is consumer-defined.
    /// </param>
    /// <remarks>
    ///     2.9: the call is first charged to the caller's remote IP address in <see cref="G9CAuthThrottle"/> (registered by
    ///     <c>AddSignalRSuperNetCoreJwt</c>, on by default, see <see cref="G9DtJwtAuthOptions"/>). A caller over its
    ///     allowance gets <c>IsAccepted = false</c> with <c>RejectionReason = "G9_RATE_LIMITED"</c>
    ///     (<see cref="G9CErrorCodes.RateLimited"/>), the credential check does not run, and a warning (event 9102) is logged.
    /// </remarks>
    public async Task Authorize(object authorizeData)
    {
        var httpContext = Context.GetHttpContext();
        var routePattern = httpContext?.Request.Path.Value;

        var throttle = httpContext?.RequestServices.GetService<G9CAuthThrottle>();
        if (throttle is not null)
        {
            var remoteIp = httpContext!.Connection.RemoteIpAddress?.ToString();
            if (!throttle.TryAcquire(remoteIp))
            {
                var logger = httpContext.RequestServices.GetService<ILogger<G9GetJwtHub>>();
                if (logger is not null) G9CAuthThrottle.LogThrottled(logger, routePattern, remoteIp);
                await SendResultAsync(MakeError(G9CErrorCodes.RateLimited)).ConfigureAwait(false);
                return;
            }
        }

        if (string.IsNullOrEmpty(routePattern))
        {
            await SendResultAsync(MakeError("Route pattern not found in the current request context."))
                .ConfigureAwait(false);
            return;
        }

        if (!G9CJwtRouteRegistry.TryGet(routePattern, out var handler) || handler is null)
        {
            await SendResultAsync(MakeError("No authorization handler is registered for this route."))
                .ConfigureAwait(false);
            return;
        }

        var (factory, extraData) = await handler(authorizeData, this).ConfigureAwait(false);
        await SendResultAsync(MakeResult(factory, extraData)).ConfigureAwait(false);
    }

    private Task SendResultAsync(G9DtAuthorizeResult result)
        => Clients.Caller.SendCoreAsync("AuthorizeResult", new object[] { result });

    private static G9DtAuthorizeResult MakeResult(G9JWTokenFactory factory, object? extraData) => new()
    {
        IsAccepted = !factory.IsRejected,
        JWToken = factory.JWToken,
        ExtraData = extraData,
        RejectionReason = factory.RejectionReason
    };

    private static G9DtAuthorizeResult MakeError(string reason) => new()
    {
        IsAccepted = false,
        JWToken = null,
        ExtraData = null,
        RejectionReason = reason
    };
}
