using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A base class for implementing SignalR Hubs with JWT authentication support.
///     This class builds on top of <see cref="G9AHubBase{TTargetClass, TClientSideMethodsInterface}" />
///     by adding methods to manage user authorization, JWT token generation, and token validation.
///     The derived classes must implement methods for handling user authentication, JWT token generation,
///     and token validation parameters.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived Hub class inheriting from
///     <see cref="G9AHubBaseWithJWTAuth{TTargetClass, TClientSideMethodsInterface}" />.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines the client-side methods which can be called from the server.
/// </typeparam>
/// <remarks>
///     The class is marked with the <see cref="AuthorizeAttribute" /> to enforce that authentication is required
///     for clients to connect and interact with the Hub.
/// </remarks>
[Authorize]
public abstract class
    G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface> : G9AHubBase<TTargetClass,
    TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
{
    #region Methods

    /// <summary>
    ///     Configures the <see cref="HttpConnectionDispatcherOptions" /> for the JWT Hub.
    /// </summary>
    /// <param name="configureOptions">
    ///     An object of <see cref="HttpConnectionDispatcherOptions" /> used to configure the dispatcher options
    ///     for this JWT-enabled Hub.
    /// </param>
    /// <remarks>
    ///     This method can be overridden to customize the behavior of HTTP connections specific to the JWT-enabled Hub.
    ///     For example, you can configure connection timeouts, transport methods, or other specific settings that
    ///     apply when JWT authentication is enabled for the Hub.
    /// </remarks>
    [HubMethodName(null)]
    [G9AttrExcludeFromClientGeneration]
    public virtual void ConfigureHubForJWTRoute(HttpConnectionDispatcherOptions configureOptions)
    {
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    ///     Defines the route pattern for the JWT-authenticated Hub. This method must be implemented
    ///     in the derived class to specify the exact route where clients can authenticate and obtain a JWT token.
    /// </summary>
    /// <returns>
    ///     A <see cref="string" /> representing the route pattern for the JWT authentication route.
    /// </returns>
    /// <remarks>
    ///     The route is used to define the URL path at which clients can request their JWT token, typically as
    ///     part of an authentication process.
    /// </remarks>
    [HubMethodName(null)]
    [G9AttrExcludeFromClientGeneration]
    public abstract string AuthAndGetJWTRoutePattern();

    /// <summary>
    ///     Validates the user credentials and generates a JWT token. This method is responsible for ensuring
    ///     the provided credentials are valid and issuing a JWT token for authenticated clients.
    /// </summary>
    /// <param name="authorizeData">
    ///     The data containing the user credentials (such as username and password or other forms of data required for
    ///     authorization).
    /// </param>
    /// <returns>
    ///     A <see cref="Task{G9JWTokenFactory}" /> representing the asynchronous operation. The task result is a
    ///     <see cref="G9JWTokenFactory" /> which contains the generated JWT token.
    /// </returns>
    /// <remarks>
    ///     This method needs to be overridden by the derived class to implement custom user validation logic
    ///     and token generation (e.g., using a database or external authentication service).
    /// </remarks>
    [HubMethodName(null)]
    [G9AttrExcludeFromClientGeneration]
    public abstract Task<G9JWTokenFactory> ValidateUserAndGenerateJWToken(object authorizeData);

    /// <summary>
    ///     Retrieves the token validation parameters for validating JWT tokens used in this Hub.
    /// </summary>
    /// <returns>
    ///     A <see cref="TokenValidationParameters" /> object that defines how tokens should be validated.
    /// </returns>
    /// <remarks>
    ///     This method is used to configure the validation parameters for the JWT token, such as the issuer,
    ///     audience, signing key, and other aspects of token validation.
    /// </remarks>
    [HubMethodName(null)]
    [G9AttrExcludeFromClientGeneration]
    public abstract TokenValidationParameters GetAuthorizeTokenValidationForHub();

    #endregion
}