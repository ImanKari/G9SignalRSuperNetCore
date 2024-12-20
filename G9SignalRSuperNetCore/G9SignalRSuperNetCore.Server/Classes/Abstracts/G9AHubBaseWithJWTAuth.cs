using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

[Authorize]
public abstract class
    G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface> : G9AHubBase<TTargetClass,
    TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
    where TClientSideMethodsInterface : class
{
    public abstract string AuthAndGetJWTRoutePattern();

    public abstract Task<G9JWTokenFactory> ValidateUserAndGenerateJWToken(object authorizeData);

    public abstract TokenValidationParameters GetAuthorizeTokenValidationForHub();

    /// <summary>
    ///     Configures the <see cref="HttpConnectionDispatcherOptions" /> for the JWT Hub.
    /// </summary>
    /// <param name="configureOptions">
    ///     An object of <see cref="HttpConnectionDispatcherOptions" /> used to configure dispatcher options.
    /// </param>
    /// <remarks>
    ///     This method can be overridden to customize the behavior of HTTP connections.
    /// </remarks>
    public virtual void ConfigureHubForJWTRoute(HttpConnectionDispatcherOptions configureOptions)
    {
    }
}