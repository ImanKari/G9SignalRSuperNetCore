using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A base class for implementing SignalR Hubs with JWT authentication support.
///     This class extends <see cref="G9AHubBase{TTargetClass, TClientSideMethodsInterface}" /> by adding methods
///     to manage user authorization, JWT token generation, and token validation for the hub.
///     The derived classes must implement methods to handle user authentication, JWT token generation,
///     and token validation parameters.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived Hub class inheriting from
///     <see cref="G9AHubBaseWithJWTAuth{TTargetClass, TClientSideMethodsInterface}" />.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines the client-side methods that can be called from the server.
/// </typeparam>
/// <remarks>
///     The class is marked with the <see cref="AuthorizeAttribute" /> to enforce that authentication is required
///     for clients to connect and interact with the Hub.
/// </remarks>
[Authorize]
public abstract class G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
    : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
{
    #region Methods

    /// <summary>
    ///     Configures the <see cref="HttpConnectionDispatcherOptions" /> for the JWT Hub.
    ///     This method can be overridden to customize the behavior of HTTP connections specific to the JWT-enabled Hub.
    ///     For example, you can configure connection timeouts, transport methods, or other specific settings that apply
    ///     when JWT authentication is enabled for the Hub.
    /// </summary>
    /// <param name="configureOptions">
    ///     An object of <see cref="HttpConnectionDispatcherOptions" /> used to configure the dispatcher options
    ///     for this JWT-enabled Hub.
    /// </param>
    /// <remarks>
    ///     This method can be overridden in derived classes to customize the behavior of HTTP connections when JWT
    ///     authentication
    ///     is enabled for the Hub.
    /// </remarks>
    [G9AttrDenyAccess]
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
    ///     This method must be implemented by derived classes to define the URL path at which clients can request their JWT
    ///     token,
    ///     typically as part of an authentication process.
    /// </remarks>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract string AuthAndGetJWTRoutePattern();

    /// <summary>
    ///     Authenticates the user based on the provided credentials and generates a JWT token for authorized clients.
    ///     This method validates the given authorization data (e.g., credentials) and, if valid, issues a JWT token.
    ///     The method is asynchronous and returns a tuple containing the JWT token factory and any additional data that may be
    ///     required.
    /// </summary>
    /// <param name="authorizeData">
    ///     The user credentials or other authorization-related data (such as username, password, or other forms of input)
    ///     that are used to authenticate the user. This data is used to verify the identity and permissions of the user
    ///     making the request.
    /// </param>
    /// <param name="accessToUnauthorizedVirtualHub">
    ///     The instance of the SignalR hub that called this method. While the method is part of the authorization hub, it
    ///     may be invoked from a different virtual hub. This parameter provides access to the hub context, connection
    ///     details, and services, allowing manipulation of the connection state or other hub-specific information
    ///     during the authentication process.
    /// </param>
    /// <returns>
    ///     A <see cref="Task{ValueTuple{G9JWTokenFactory, object?}}" /> representing the asynchronous operation.
    ///     The result is a tuple containing:
    ///     <para />
    ///     - A <see cref="G9JWTokenFactory" /> instance with the generated JWT token if authentication is successful.
    ///     <para />
    ///     - Any additional data related to the authentication process, which may be returned as `null` if not applicable.
    /// </returns>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(object authorizeData,
        Hub accessToUnauthorizedVirtualHub);


    /// <summary>
    ///     Retrieves the token validation parameters for validating JWT tokens used in this Hub.
    /// </summary>
    /// <returns>
    ///     A <see cref="TokenValidationParameters" /> object that defines how tokens should be validated.
    /// </returns>
    /// <remarks>
    ///     This method is used to configure the validation parameters for the JWT token, such as the issuer, audience, signing
    ///     key,
    ///     and other aspects of token validation.
    /// </remarks>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract TokenValidationParameters GetAuthorizeTokenValidationForHub();

    #endregion
}