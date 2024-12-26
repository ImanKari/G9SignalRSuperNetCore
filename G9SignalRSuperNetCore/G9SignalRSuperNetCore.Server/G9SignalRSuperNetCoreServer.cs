using System.Security.Claims;
using System.Text.Json;
using G9AssemblyManagement;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Classes.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server;

/// <summary>
///     Provides extensions to configure SignalR SuperNetCore server services and hubs.
/// </summary>
public static class G9SignalRSuperNetCoreServer
{
    /// <summary>
    ///     Flag to track if the basic services (such as custom <see cref="IUserIdProvider" /> and authorization setup) have
    ///     been added.
    ///     This ensures that the basic services are only registered once in the dependency injection container.
    /// </summary>
    private static bool _addBasicService;

    /// <summary>
    ///     Flag to track if JWT Bearer authentication has been added.
    ///     This ensures that JWT authentication is only registered once, even if multiple hubs are configured.
    /// </summary>
    private static bool _addJwtService;

    /// <summary>
    ///     A dictionary to store token validation parameters for different SignalR hub paths.
    ///     The key is the <see cref="string" /> hub path, and the value is the associated
    ///     <see cref="TokenValidationParameters" />.
    ///     This allows for different token validation configurations for each SignalR hub, enabling support for multiple hubs
    ///     with different authentication schemes or routes.
    /// </summary>
    private static readonly Dictionary<string, TokenValidationParameters> HubTokenValidationParameters = new();

    /// <summary>
    ///     Determines if the target hub class is derived from
    ///     <see cref="G9AHubBaseWithJWTAuth{TTargetClass,TClientSideMethodsInterface,TAuthenticationDataType}" />.
    /// </summary>
    /// <param name="targetType">The type of the target class to check.</param>
    /// <returns>
    ///     True if the target class inherits from
    ///     <see cref="G9AHubBaseWithJWTAuth{TTargetClass,TClientSideMethodsInterface, TAuthenticationDataType}" />, otherwise
    ///     false.
    /// </returns>
    private static bool IsInheritedFromG9AHubBaseWithJWTAuth(Type targetType)
    {
        // The base generic type definition
        var baseType = typeof(G9AHubBaseWithJWTAuth<,,>);
        // Traverse the inheritance chain to check if targetType or its base types
        // are a closed type of G9AHubBaseWithJWTAuth<,,>
        while (targetType != null)
        {
            // Check if the current type is a closed generic type
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == baseType) return true;

            // Move to the base type in the inheritance hierarchy
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            targetType = targetType.BaseType;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
        }

        return false;
    }

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
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        var targetClass = G9Assembly.InstanceTools.CreateUninitializedInstanceFromType<TTargetClass>();

        if (!_addBasicService)
        {
            _addBasicService = true;
            // Configure custom UserIdProvider
            services.AddSingleton<IUserIdProvider, G9CUserIdProvider>(_ => new G9CUserIdProvider(userIdentifier));

            services.AddAuthorization(options =>
            {
                options.AddPolicy(G9CAlwaysDenyRequirement.DenyPolicyName,
                    policy => { policy.Requirements.Add(new G9CAlwaysDenyRequirement()); });
            });

            services.AddSingleton<IAuthorizationHandler, G9CAlwaysDenyHandler>();
        }

        // Handle JWT Authentication only once
        if (IsInheritedFromG9AHubBaseWithJWTAuth(typeof(TTargetClass)))
        {
            var methods = G9Assembly.ReflectionTools.GetMethodsOfObject(targetClass);

            var auth = methods.Single(s => s.MethodName == "GetAuthorizeTokenValidationForHub")
                .CallMethodWithResult<TokenValidationParameters>();
            var hubPath = methods.Single(s => s.MethodName == "RoutePattern")
                .CallMethodWithResult<string>();

            // Store the hub path and its corresponding authentication configuration
            HubTokenValidationParameters[hubPath] = auth;

            if (!_addJwtService)
            {
                _addJwtService = true;
                // Add JWT Bearer Authentication
                services.AddAuthentication("Bearer")
                    .AddJwtBearer("Bearer", options =>
                    {
                        options.Events = new JwtBearerEvents
                        {
                            OnMessageReceived = context =>
                            {
                                var accessToken = context.Request.Query["access_token"];

                                // Check if the request is for one of the hubs
                                foreach (var path in HubTokenValidationParameters.Keys.Where(path =>
                                             context.HttpContext.Request.Path.StartsWithSegments(path)))
                                {
                                    // Assign the appropriate token validation parameters based on the hub path
                                    options.TokenValidationParameters = HubTokenValidationParameters[path];

                                    if (!string.IsNullOrEmpty(accessToken)) context.Token = accessToken;

                                    break;
                                }

                                return Task.CompletedTask;
                            }
                        };
                    });
            }

            // Add authorization services if not already done
            services.AddAuthorization();
        }

        // Add and configure SignalR options
        services.AddSignalR(option =>
        {
            // Set general SignalR options (message size, timeouts, etc.)
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
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        var targetClass = G9Assembly.InstanceTools.CreateUninitializedInstanceFromType<TTargetClass>();


        if (IsInheritedFromG9AHubBaseWithJWTAuth(typeof(TTargetClass)))
        {
            var methods = G9Assembly.ReflectionTools.GetMethodsOfObject(targetClass);

            // Fetch the route and authentication methods
            var jwtRoutePattern = methods.Single(s => s.MethodName == "AuthAndGetJWTRoutePattern")
                .CallMethodWithResult<string>();
            var authenticateMethod = methods.Single(s => s.MethodName == "AuthenticateAndGenerateJwtTokenAsync");
            var genericParameterType = authenticateMethod.MethodInfo.GetParameters()[0].ParameterType;

            // Add authentication route with JWT token generation
            if (!G9GetJwtHub._validateUserAndGenerateJWTokenPerRoute.ContainsKey(jwtRoutePattern))
                _ = G9GetJwtHub._validateUserAndGenerateJWTokenPerRoute.TryAdd(jwtRoutePattern,
                    (authenticationData, hub) =>
                        authenticateMethod.CallMethodWithResult<Task<G9JWTokenFactory>>(JsonSerializer.Deserialize(
                            authenticationData, genericParameterType, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            }), hub));


            app.MapHub<G9GetJwtHub>(jwtRoutePattern,
                options => methods.Single(s => s.MethodName == "ConfigureHubForJWTRoute").CallMethod(options));


            // Map the Hub to its defined route and apply configurations
            app.MapHub<TTargetClass>(targetClass.RoutePattern(), targetClass.ConfigureHub).RequireAuthorization();
        }
        else
        {
            // Map the Hub to its defined route and apply configurations
            app.MapHub<TTargetClass>(targetClass.RoutePattern(), targetClass.ConfigureHub);
        }
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