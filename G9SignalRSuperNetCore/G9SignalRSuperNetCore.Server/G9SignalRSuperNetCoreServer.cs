using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Classes.Hubs;
using G9SignalRSuperNetCore.Server.Classes.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Server;

/// <summary>
///     Provides extension methods to configure SignalR SuperNetCore server services and hubs.
/// </summary>
/// <remarks>
///     <para>
///         All registration APIs in this class are AOT- and trim-safe. Hubs are registered
///         through the dependency injection container; no runtime reflection or uninitialized
///         instance creation is required.
///     </para>
///     <para>
///         Configuration helpers (such as <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}.RoutePattern"/>
///         and the JWT validation hooks) are now passed to the registration methods directly,
///         instead of being read from a synthetic instance.
///     </para>
/// </remarks>
public static class G9SignalRSuperNetCoreServer
{
    private static int _basicServiceAdded;
    private static int _jwtServiceAdded;

    private static readonly Dictionary<string, TokenValidationParameters> HubTokenValidationParameters
        = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds the SignalR SuperNetCore core services (custom UserIdProvider, deny policy, SignalR options).
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="userIdentifier">An optional custom function that maps a connection to a user identifier.</param>
    /// <param name="configureSignalROptions">An optional callback to customize <see cref="HubOptions"/>.</param>
    public static IServiceCollection AddSignalRSuperNetCoreCore(
        this IServiceCollection services,
        Func<HubConnectionContext, string?>? userIdentifier = null,
        Action<HubOptions>? configureSignalROptions = null)
    {
        if (Interlocked.Exchange(ref _basicServiceAdded, 1) == 0)
        {
            services.AddSingleton<IUserIdProvider>(_ => new G9CUserIdProvider(userIdentifier));

            services.AddAuthorization(options =>
            {
                options.AddPolicy(G9CAlwaysDenyRequirement.DenyPolicyName,
                    policy => policy.Requirements.Add(new G9CAlwaysDenyRequirement()));
            });

            services.AddSingleton<IAuthorizationHandler, G9CAlwaysDenyHandler>();

            // Singleton hub filter that enforces every G9 attribute (rate limit, connection
            // limit, role/claim requirements, telemetry). Hubs that use no attributes pay
            // only the cost of one dictionary lookup per call.
            services.AddSingleton<G9CHubFilter>();

            services.AddSignalR(option =>
            {
                option.KeepAliveInterval = TimeSpan.FromSeconds(10);
                option.ClientTimeoutInterval = TimeSpan.FromSeconds(60);

                // SignalR's default MaximumReceiveMessageSize is 32 KB, which is too small for
                // the resumable file-upload feature. The default JSON HubProtocol base64-encodes
                // byte[] arguments, expanding a 64 KB binary chunk to ~85 KB on the wire — that
                // exceeds 32 KB and the server silently aborts the connection on the first chunk.
                // Bumping the cap to 4 MB lets clients pick chunk sizes up to ~3 MB without any
                // additional configuration. Consumers can still override this via configureSignalROptions.
                option.MaximumReceiveMessageSize = 4 * 1024 * 1024;

                option.AddFilter<G9CHubFilter>();
                configureSignalROptions?.Invoke(option);
            });
        }

        return services;
    }

    /// <summary>
    ///     Registers an in-memory <see cref="IG9SessionStore{TSession}"/> for the given session type.
    ///     Replace this registration with a distributed implementation for horizontal scale-out.
    /// </summary>
    public static IServiceCollection AddG9SignalRSuperNetCoreSessionStore<TSession>(
        this IServiceCollection services)
        where TSession : G9ASession, new()
    {
        services.AddSingleton<IG9SessionStore<TSession>, G9CInMemorySessionStore<TSession>>();
        return services;
    }

    /// <summary>
    ///     Registers the resumable file-upload service. Files are stored under
    ///     <see cref="G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadOptions.RootDirectory"/>
    ///     (default <c>./uploads</c>) with partials in a hidden subfolder.
    /// </summary>
    /// <param name="services">DI service collection.</param>
    /// <param name="configure">Optional callback to customize <see cref="G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadOptions"/>.</param>
    public static IServiceCollection AddG9SignalRSuperNetCoreFileUpload(
        this IServiceCollection services,
        Action<G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadOptions>? configure = null)
    {
        if (configure is not null) services.Configure(configure);
        else services.AddOptions<G9SignalRSuperNetCore.Server.Classes.FileUpload.G9DtUploadOptions>();

        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.FileUpload.IG9UploadService,
            G9SignalRSuperNetCore.Server.Classes.FileUpload.G9CUploadService>();
        return services;
    }

    /// <summary>
    ///     Adds the SignalR SuperNetCore server services for an unauthenticated hub.
    /// </summary>
    /// <typeparam name="TTargetClass">The hub type derived from <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}"/>.</typeparam>
    /// <typeparam name="TClientSideMethodsInterface">The interface defining client-side methods that the server can invoke.</typeparam>
    /// <param name="services">The DI service collection.</param>
    /// <param name="userIdentifier">An optional custom function that maps a connection to a user identifier.</param>
    /// <param name="configureSignalROptions">An optional callback to customize <see cref="HubOptions"/>.</param>
    public static IServiceCollection AddSignalRSuperNetCoreServerService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        TTargetClass, TClientSideMethodsInterface>(
        this IServiceCollection services,
        Func<HubConnectionContext, string?>? userIdentifier = null,
        Action<HubOptions>? configureSignalROptions = null)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        services.AddSignalRSuperNetCoreCore(userIdentifier, configureSignalROptions);
        return services;
    }

    /// <summary>
    ///     Configures JWT bearer authentication for one or more SignalR hubs registered through this library.
    ///     Multiple hub registrations contribute their <see cref="TokenValidationParameters"/> through the same handler.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="hubPath">The route path of the protected hub, e.g. <c>/SecureHub</c>.</param>
    /// <param name="validationParameters">The validation parameters that apply to <paramref name="hubPath"/>.</param>
    public static IServiceCollection AddSignalRSuperNetCoreJwt(
        this IServiceCollection services,
        string hubPath,
        TokenValidationParameters validationParameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(hubPath);
        ArgumentNullException.ThrowIfNull(validationParameters);

        HubTokenValidationParameters[hubPath] = validationParameters;

        if (Interlocked.Exchange(ref _jwtServiceAdded, 1) == 0)
        {
            services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", options =>
                {
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            foreach (var path in HubTokenValidationParameters.Keys)
                            {
                                if (context.HttpContext.Request.Path.StartsWithSegments(path))
                                {
                                    options.TokenValidationParameters = HubTokenValidationParameters[path];
                                    if (!string.IsNullOrEmpty(accessToken)) context.Token = accessToken;
                                    break;
                                }
                            }

                            return Task.CompletedTask;
                        }
                    };
                });

            services.AddAuthorization();
        }

        return services;
    }

    /// <summary>
    ///     Maps an unauthenticated SignalR hub at its declared route pattern.
    /// </summary>
    public static IEndpointConventionBuilder AddSignalRSuperNetCoreServerHub<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app,
        string routePattern,
        Action<HttpConnectionDispatcherOptions>? configureHub = null)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        ArgumentException.ThrowIfNullOrEmpty(routePattern);
        return app.MapHub<TTargetClass>(routePattern, options => configureHub?.Invoke(options));
    }

    /// <summary>
    ///     Maps a JWT-protected SignalR hub. Registers both the auth route and the protected hub route,
    ///     stores the route's authentication delegate, and applies the route's
    ///     <see cref="TokenValidationParameters"/>.
    /// </summary>
    public static void AddSignalRSuperNetCoreJwtHub<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app,
        string hubRoutePattern,
        string authRoutePattern,
        Func<object, Hub, Task<(G9JWTokenFactory, object?)>> authenticate,
        Action<HttpConnectionDispatcherOptions>? configureHub = null,
        Action<HttpConnectionDispatcherOptions>? configureAuthHub = null)
        where TTargetClass : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        ArgumentException.ThrowIfNullOrEmpty(hubRoutePattern);
        ArgumentException.ThrowIfNullOrEmpty(authRoutePattern);
        ArgumentNullException.ThrowIfNull(authenticate);

        G9CJwtRouteRegistry.Register(authRoutePattern, authenticate);

        app.MapHub<G9GetJwtHub>(authRoutePattern, options => configureAuthHub?.Invoke(options));
        app.MapHub<TTargetClass>(hubRoutePattern, options => configureHub?.Invoke(options))
            .RequireAuthorization();
    }

    /// <summary>
    ///     A custom <see cref="IUserIdProvider"/> implementation.
    /// </summary>
    private sealed class G9CUserIdProvider : IUserIdProvider
    {
        private readonly Func<HubConnectionContext, string?>? _userIdentifier;

        public G9CUserIdProvider(Func<HubConnectionContext, string?>? userIdentifier)
        {
            _userIdentifier = userIdentifier;
        }

        public string? GetUserId(HubConnectionContext connection)
        {
            if (_userIdentifier != null) return _userIdentifier(connection);
            if (connection.User.FindFirst(ClaimTypes.NameIdentifier) is { } nameIdentifier)
                return nameIdentifier.Value;
            return connection.UserIdentifier;
        }
    }
}
