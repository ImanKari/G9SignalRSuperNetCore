using System.Security.Claims;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Server;

/// <summary>
///     Provides extensions to configure SignalR SuperNetCore server services and hubs.
/// </summary>
public static class G9SignalRSuperNetCoreServer
{
    /// <summary>
    ///     Adds the SignalR SuperNetCore server services to the dependency injection container.
    /// </summary>
    /// <typeparam name="TTargetClass">
    ///     The type of the Hub class derived from <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}" />.
    /// </typeparam>
    /// <typeparam name="TClientSideMethodsInterface">
    ///     The interface representing client-side methods that the server can invoke.
    /// </typeparam>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> to add the SignalR services to.
    /// </param>
    /// <param name="userIdentifier">
    ///     A function that provides custom user identifiers for SignalR connections.
    /// </param>
    /// <remarks>
    ///     Configures options such as message size, keep-alive intervals, and client timeouts.
    ///     It also sets up a custom <see cref="IUserIdProvider" /> for user identification.
    /// </remarks>
    public static void AddSignalRSuperNetCoreServerService<TTargetClass, TClientSideMethodsInterface>(
        this IServiceCollection services, Func<HubConnectionContext, string?>? userIdentifier = null)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
        where TClientSideMethodsInterface : class
    {
        var targetClass = new TTargetClass();

        // Configure custom UserIdProvider
        services.AddSingleton<IUserIdProvider, G9CUserIdProvider>(_ => new G9CUserIdProvider(userIdentifier));

        // Add and configure SignalR options
        services.AddSignalR(option =>
        {
            option.MaximumReceiveMessageSize = 200 * 1024 * 1024; // Set maximum message size to 200MB
            option.KeepAliveInterval = TimeSpan.FromSeconds(10); // Ping clients every 10 seconds
            option.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Set client timeout to 60 seconds

            // Allow the target class to customize SignalR options
            targetClass.ConfigureHubOption(option);
        });
    }

    /// <summary>
    ///     Maps the SignalR hub for the given <typeparamref name="TTargetClass" /> to the specified endpoint.
    /// </summary>
    /// <typeparam name="TTargetClass">
    ///     The type of the Hub class derived from <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}" />.
    /// </typeparam>
    /// <typeparam name="TClientSideMethodsInterface">
    ///     The interface representing client-side methods that the server can invoke.
    /// </typeparam>
    /// <param name="app">
    ///     The <see cref="IEndpointRouteBuilder" /> to map the hub endpoint.
    /// </param>
    /// <remarks>
    ///     The hub is mapped to the route specified by the
    ///     <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}.RoutePattern" /> method.
    ///     Additional hub configuration can be applied via
    ///     <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}.ConfigureHub" />.
    /// </remarks>
    public static void AddSignalRSuperNetCoreServerHub<TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
        where TClientSideMethodsInterface : class
    {
        var targetClass = new TTargetClass();

        // Map the Hub to its defined route and apply configurations
        app.MapHub<TTargetClass>(targetClass.RoutePattern(), targetClass.ConfigureHub);
    }

    /// <summary>
    ///     A custom implementation of <see cref="IUserIdProvider" /> for identifying SignalR users.
    /// </summary>
    public class G9CUserIdProvider : IUserIdProvider
    {
        private readonly Func<HubConnectionContext, string?>? _userIdentifier;

        /// <summary>
        ///     Initializes a new instance of the <see cref="G9CUserIdProvider" /> class.
        /// </summary>
        /// <param name="userIdentifier">
        ///     A function that provides user identifiers based on the <see cref="HubConnectionContext" />.
        /// </param>
        public G9CUserIdProvider(Func<HubConnectionContext, string?>? userIdentifier)
        {
            _userIdentifier = userIdentifier;
        }

        /// <summary>
        ///     Gets the user ID for the given SignalR connection context.
        /// </summary>
        /// <param name="connection">The <see cref="HubConnectionContext" /> for the current connection.</param>
        /// <returns>
        ///     The user ID as a <see cref="string" />. Returns <c>null</c> if no user ID can be determined.
        /// </returns>
        public string? GetUserId(HubConnectionContext connection)
        {
            // Use the custom user identifier function if provided
            if (_userIdentifier != null)
                return _userIdentifier(connection);

            // Fallback: use the NameIdentifier claim from the user principal
            if (connection.User.FindFirst(ClaimTypes.NameIdentifier) is { } nameIdentifier)
                return nameIdentifier.Value;

            // Default: use the UserIdentifier provided by SignalR
            return connection.UserIdentifier;
        }
    }
}