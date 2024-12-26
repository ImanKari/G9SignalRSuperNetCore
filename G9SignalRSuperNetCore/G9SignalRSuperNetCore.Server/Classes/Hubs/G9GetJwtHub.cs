using System.Collections.Concurrent;
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
    /// </remarks>
    internal static ConcurrentDictionary<string, Func<dynamic, Hub, Task<G9JWTokenFactory>>>
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
    ///     A task representing the asynchronous operation, with a result indicating whether the authorization was successful
    ///     and providing the generated JWT token if applicable.
    /// </returns>
    public async Task Authorize(object authorizeData)
    {
        // Retrieve the current request's route pattern from the SignalR context
        var routePattern = Context.GetHttpContext()?.Request.Path;

        if (string.IsNullOrEmpty(routePattern))
            throw new InvalidOperationException(
                "Route pattern not found in the current request context. `Context.GetHttpContext()?.Request.Path`");

        // Check if there's a function registered for this route pattern
        if (_validateUserAndGenerateJWTokenPerRoute.TryGetValue(routePattern, out var func))
        {
            // Call the function to validate the authorization data and generate the JWT token
            var result = await func(authorizeData, this);

            // If the result is rejected, send a failure response to the client
            if (result.IsRejected)
                await Clients.Caller.SendCoreAsync("AuthorizeResult",
                    new object[] { false, result.RejectionReason!, null! });
            else
                // If authorized, send a success response with the generated JWT token
                await Clients.Caller.SendCoreAsync("AuthorizeResult", new object[] { true, null!, result.JWToken! });
        }
        else
        {
            // If no function is registered for this route, send an error response
            await Clients.Caller.SendCoreAsync("AuthorizeResult",
                new object[] { false, "No authorization handler for this route", null! });
        }
    }
}