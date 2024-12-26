using System.Collections.Concurrent;
using G9SignalRSuperNetCore.Server.Classes.DataTypes;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs;

/// <summary>
///     A SignalR Hub for handling JWT authorization requests and token generation.
///     This class provides functionality to authorize users by validating their request data
///     and generating a JWT token for authorized users.
/// </summary>
public class G9GetJwtHub : Hub
{
    /// <summary>
    ///     A static dictionary that holds functions for validating user data and generating JWT tokens for different routes.
    ///     The key is the route pattern, and the value is a function that validates the authorization data and generates a JWT
    ///     token.
    /// </summary>
    /// <remarks>
    ///     The functions in this dictionary are responsible for handling different route-specific authorization logic.
    ///     Each function takes the authorization data and a Hub context, and returns a tuple containing the JWT token factory
    ///     and any extra data that may be sent back to the client.
    /// </remarks>
    internal static ConcurrentDictionary<string, Func<object, Hub, Task<(G9JWTokenFactory, object?)>>>
        _validateUserAndGenerateJWTokenPerRoute = new();

    /// <summary>
    ///     Authorizes the user based on the provided authorization data.
    ///     This method looks up the appropriate function to validate the user and generate a JWT token,
    ///     based on the current route pattern.
    /// </summary>
    /// <param name="authorizeData">
    ///     The authorization data sent by the client. This data is used for validating the user's credentials or permissions.
    /// </param>
    /// <returns>
    ///     A task representing the asynchronous operation. Upon completion, it sends the authorization result to the client,
    ///     including whether the authorization was successful, the rejection reason (if any), and the generated JWT token (if
    ///     applicable).
    /// </returns>
    public async Task Authorize(object authorizeData)
    {
        // Retrieve the current request's route pattern from the SignalR context
        var routePattern = Context.GetHttpContext()?.Request.Path;

        // Ensure the route pattern is valid
        if (string.IsNullOrEmpty(routePattern))
            throw new InvalidOperationException(
                "Route pattern not found in the current request context. `Context.GetHttpContext()?.Request.Path`");

        // Check if there's a function registered for this route pattern
        if (_validateUserAndGenerateJWTokenPerRoute.TryGetValue(routePattern, out var func))
        {
            // Call the function to validate the authorization data and generate the JWT token
            var result = await func(authorizeData, this);

            await Clients.Caller.SendCoreAsync("AuthorizeResult", new object[] { PrepareAuthorizeData(result) });
        }
        else
        {
            // If no function is registered for this route, send an error response
            await Clients.Caller.SendCoreAsync("AuthorizeResult",
                new object[] { PrepareErrorData("No authorization handler for this route") });
        }
    }

    /// <summary>
    ///     Prepares the authorization result data to be sent to the client.
    ///     This method creates a `G9DtAuthorizeResult` object containing the authorization outcome,
    ///     including the rejection reason (if any) and the generated JWT token.
    /// </summary>
    /// <param name="param">
    ///     A tuple containing the `G9JWTokenFactory` (which contains the JWT token) and any additional data to be sent to the
    ///     client.
    /// </param>
    /// <returns>
    ///     A `G9DtAuthorizeResult` object containing the result of the authorization, including:
    ///     - `IsAccepted`: Whether the authorization was accepted.
    ///     - `RejectionReason`: The reason for rejection if not accepted.
    ///     - `JWToken`: The generated JWT token if authorized.
    ///     - `ExtraData`: Any additional data to be sent to the client.
    /// </returns>
    private G9DtAuthorizeResult PrepareAuthorizeData((G9JWTokenFactory, object?) param)
    {
        return new G9DtAuthorizeResult
        {
            IsAccepted =
                !param.Item1.IsRejected, // Corrected: Indicates if the authorization was accepted (not IsRejected)
            JWToken = param.Item1.JWToken, // The JWT token generated for the user if authorized
            ExtraData = param.Item2, // Any additional data to send to the client, can be null
            RejectionReason = param.Item1.RejectionReason // Reason for rejection if not authorized
        };
    }

    /// <summary>
    ///     Prepares error data to be sent when the authorization fails.
    ///     This method creates a `G9DtAuthorizeResult` object with a rejection reason and marks the authorization as failed.
    /// </summary>
    /// <param name="rejectionReason">
    ///     The reason for the rejection of the authorization request.
    /// </param>
    /// <returns>
    ///     A `G9DtAuthorizeResult` object indicating that the authorization was rejected and providing the rejection reason.
    /// </returns>
    private G9DtAuthorizeResult PrepareErrorData(string rejectionReason)
    {
        return new G9DtAuthorizeResult
        {
            IsAccepted = false, // The authorization is rejected
            JWToken = null, // No JWT token generated
            ExtraData = null, // No additional data
            RejectionReason = rejectionReason // Reason for rejection
        };
    }
}