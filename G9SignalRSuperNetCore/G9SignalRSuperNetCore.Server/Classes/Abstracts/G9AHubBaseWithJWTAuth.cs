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
///
///     This class also introduces a new generic type <typeparamref name="TAuthenticationDataType"/> 
///     which specifies the type of the authentication data that is used for validating and generating JWT tokens.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived Hub class inheriting from <see cref="G9AHubBaseWithJWTAuth{TTargetClass, TClientSideMethodsInterface, TAuthenticationDataType}" />.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines the client-side methods that can be called from the server.
/// </typeparam>
/// <typeparam name="TAuthenticationDataType">
///     A class that defines the type of the authentication data used for user validation and JWT token generation.
///     This type is passed into methods like <see cref="AuthenticateAndGenerateJwtTokenAsync"/> to provide the 
///     necessary user credentials or other forms of data for authentication.
/// </typeparam>
/// <remarks>
///     The class is marked with the <see cref="AuthorizeAttribute" /> to enforce that authentication is required
///     for clients to connect and interact with the Hub.
/// </remarks>
[Authorize]
public abstract class G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface, TAuthenticationDataType>
    : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
    where TClientSideMethodsInterface : class
    where TAuthenticationDataType : class
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
    ///     This method can be overridden in derived classes to customize the behavior of HTTP connections when JWT authentication 
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
    ///     This method must be implemented by derived classes to define the URL path at which clients can request their JWT token, 
    ///     typically as part of an authentication process.
    /// </remarks>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract string AuthAndGetJWTRoutePattern();

    /// <summary>
    ///     Validates the user credentials and generates a JWT token. This method is responsible for ensuring
    ///     the provided credentials (of type <typeparamref name="TAuthenticationDataType" />) are valid and issuing
    ///     a JWT token for authenticated clients.
    /// </summary>
    /// <param name="authorizeData">
    ///     The data containing the user credentials (such as username, password, or other forms of data required for
    ///     authorization). This parameter, of type <typeparamref name="TAuthenticationDataType" />, is used to verify 
    ///     the identity and permissions of the user making the request.
    /// </param>
    /// <param name="accessToUnauthorizedVirtualHub">
    ///     The instance of the virtual hub from which this method is called. Although this method is defined in the
    ///     authorization hub, it is invoked by a virtual hub. The parameter provides access to hub-specific data like
    ///     the context of the hub, connection details, and other services. It allows you to access and manipulate
    ///     the connection state or other hub-related information during the authentication process.
    /// </param>
    /// <returns>
    ///     A <see cref="Task{G9JWTokenFactory}" /> representing the asynchronous operation. The task result is a
    ///     <see cref="G9JWTokenFactory" /> that contains the generated JWT token if authentication is successful.
    /// </returns>
    /// <remarks>
    ///     This method must be overridden in derived classes to implement custom user validation logic and token generation (e.g., 
    ///     using a database or external authentication service). The method accepts <typeparamref name="TAuthenticationDataType" />
    ///     as the authentication data for validation and token creation.
    /// </remarks>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract Task<G9JWTokenFactory> AuthenticateAndGenerateJwtTokenAsync(TAuthenticationDataType authorizeData,
        Hub accessToUnauthorizedVirtualHub);

    /// <summary>
    ///     Retrieves the token validation parameters for validating JWT tokens used in this Hub.
    /// </summary>
    /// <returns>
    ///     A <see cref="TokenValidationParameters" /> object that defines how tokens should be validated.
    /// </returns>
    /// <remarks>
    ///     This method is used to configure the validation parameters for the JWT token, such as the issuer, audience, signing key, 
    ///     and other aspects of token validation.
    /// </remarks>
    [G9AttrDenyAccess]
    [G9AttrExcludeFromClientGeneration]
    public abstract TokenValidationParameters GetAuthorizeTokenValidationForHub();

    #endregion
}
