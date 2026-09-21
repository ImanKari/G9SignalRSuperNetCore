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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    private static readonly Dictionary<string, TokenValidationParameters> HubTokenValidationParameters
        = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds the SignalR SuperNetCore core services (custom UserIdProvider, deny policy, SignalR options).
    ///     Idempotent per service collection: a second call on the same collection does nothing, while every
    ///     new collection (a second host, a test server) gets its own registrations.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="userIdentifier">An optional custom function that maps a connection to a user identifier.</param>
    /// <param name="configureSignalROptions">An optional callback to customize <see cref="HubOptions"/>.</param>
    public static IServiceCollection AddSignalRSuperNetCoreCore(
        this IServiceCollection services,
        Func<HubConnectionContext, string?>? userIdentifier = null,
        Action<HubOptions>? configureSignalROptions = null)
    {
        if (TryMark<G9CCoreServicesMarker>(services))
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
    ///     Registers the in-process group manager for <typeparamref name="THub"/>. Hubs that need
    ///     "who's in this group?" queries (or auto-join via
    ///     <see cref="G9SignalRSuperNetCore.Server.Classes.Attributes.G9AttrAutoJoinGroupAttribute"/>)
    ///     should call this.
    /// </summary>
    /// <typeparam name="THub">The SignalR hub type whose groups are managed.</typeparam>
    public static IServiceCollection AddG9SignalRSuperNetCoreGroups<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        THub>(this IServiceCollection services)
        where THub : Hub
    {
        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Groups.G9CGroupManager<THub>>();
        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Groups.G9CAutoJoinFilter<THub>>();

        // Plug the hub-typed auto-join filter into the per-hub SignalR options so it doesn't run
        // for every hub registered in the process. AOT-safe because THub is fully resolved at
        // compile time.
        //
        // IMPORTANT: SignalR REPLACES (does not merge) the global HubOptions.HubFilters list once
        // any per-hub filter is added through HubOptions<THub>. So we must re-add G9CHubFilter
        // here too — otherwise rate-limit, connection-limit, role/claim, telemetry, and presence
        // policies would silently stop applying to THub.
        services.AddSignalR().AddHubOptions<THub>(o =>
        {
            o.AddFilter<G9CHubFilter>();
            o.AddFilter<G9SignalRSuperNetCore.Server.Classes.Groups.G9CAutoJoinFilter<THub>>();
        });

        return services;
    }

    /// <summary>
    ///     Registers the process-local presence tracker. Hubs decorated with
    ///     <see cref="G9SignalRSuperNetCore.Server.Classes.Attributes.G9AttrPresenceTrackedAttribute"/>
    ///     will publish online / offline transitions through
    ///     <c>G9CPresenceTracker.Events</c>.
    /// </summary>
    public static IServiceCollection AddG9SignalRSuperNetCorePresence(this IServiceCollection services)
    {
        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Presence.G9CPresenceTracker>();
        return services;
    }

    /// <summary>
    ///     Registers the lightweight ECDH-static + ChaCha20-Poly1305 handshake helper as a
    ///     singleton. Hubs that need app-level encryption over a plain (non-TLS) SignalR
    ///     connection should call this and expose a hub method that returns the server's
    ///     static public key (<c>G9CHandshake.StaticPublicKey</c>).
    /// </summary>
    public static IServiceCollection AddG9SignalRSuperNetCoreHandshake(this IServiceCollection services)
    {
        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Crypto.G9CHandshake>();
        services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Crypto.G9CSessionSealer>();
        return services;
    }

    /// <summary>
    ///     Registers an <see cref="G9SignalRSuperNetCore.Server.Classes.Distributed.IG9DistributedBackplane"/>
    ///     for cross-node coordination. The default implementation is a no-op suitable for a
    ///     single-process deployment; pass a custom factory to wire up Redis, NATS, or another
    ///     transport when scaling out.
    /// </summary>
    public static IServiceCollection AddG9SignalRSuperNetCoreBackplane(
        this IServiceCollection services,
        Func<IServiceProvider, G9SignalRSuperNetCore.Server.Classes.Distributed.IG9DistributedBackplane>? factory = null)
    {
        if (factory is null)
            services.AddSingleton<G9SignalRSuperNetCore.Server.Classes.Distributed.IG9DistributedBackplane,
                G9SignalRSuperNetCore.Server.Classes.Distributed.G9CInProcessBackplane>();
        else
            services.AddSingleton(factory);
        return services;
    }

    /// <summary>
    ///     Sets the server's stateful-reconnect buffer: how many bytes of sent messages the server keeps PER
    ///     CONNECTION that negotiated stateful reconnect, to replay them after the client resumes. SignalR's default
    ///     is 100,000; when the buffer is full the server stops sending on that connection until the client
    ///     acknowledges. Optional — it only matters for endpoints mapped with <c>allowStatefulReconnects: true</c>,
    ///     and multiplies by the number of such connections.
    /// </summary>
    /// <remarks>
    ///     Applies to every hub, including one that has options of its own (<c>AddHubOptions&lt;THub&gt;</c>, which
    ///     <see cref="AddG9SignalRSuperNetCoreGroups{THub}"/> uses): SignalR itself does not carry this value from the
    ///     global options into those, so the library does. A hub whose own options set a size keeps it.
    /// </remarks>
    /// <param name="services">DI service collection.</param>
    /// <param name="bufferSizeBytes">The buffer size in bytes; must be positive.</param>
    public static IServiceCollection AddG9SignalRSuperNetCoreStatefulReconnect(
        this IServiceCollection services,
        long bufferSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSizeBytes);

        // A hub without options of its own reads the global HubOptions.
        services.Configure<HubOptions>(options => options.StatefulReconnectBufferSize = bufferSizeBytes);

        // A hub WITH its own HubOptions<THub> (AddHubOptions<THub>, which AddG9SignalRSuperNetCoreGroups<THub> uses for its
        // filters) reads those instead. SignalR seeds them from the global options member by member and — up to
        // ASP.NET Core 10 — leaves StatefulReconnectBufferSize out, so the value above would silently not apply to it.
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IPostConfigureOptions<>), typeof(G9CStatefulReconnectBufferSetup<>)));
        return services;
    }

    /// <summary>
    ///     Carries the global <see cref="HubOptions.StatefulReconnectBufferSize"/> into a hub's own
    ///     <see cref="HubOptions{THub}"/> unless that hub was given a size of its own. Registered as an open generic
    ///     because the hub types are not known here; it looks at every options type once and leaves all others alone.
    /// </summary>
    private sealed class G9CStatefulReconnectBufferSetup<TOptions>(IServiceProvider services) : IPostConfigureOptions<TOptions>
        where TOptions : class
    {
        private static readonly long SignalRDefault = new HubOptions().StatefulReconnectBufferSize;

        public void PostConfigure(string? name, TOptions options)
        {
            // Not the global options themselves: they are the source (and resolving them from here would recurse).
            if (options is not HubOptions perHub || options.GetType() == typeof(HubOptions)) return;
            if (perHub.StatefulReconnectBufferSize != SignalRDefault) return;   // this hub was given its own size
            perHub.StatefulReconnectBufferSize = services.GetRequiredService<IOptions<HubOptions>>().Value.StatefulReconnectBufferSize;
        }
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

        if (TryMark<G9CJwtServicesMarker>(services))
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
    ///     Maps an unauthenticated SignalR hub at its declared route pattern and says whether the endpoint accepts
    ///     SignalR's stateful reconnect (ASP.NET Core 8+). The overload without the parameter leaves it off.
    /// </summary>
    /// <remarks>
    ///     Stateful reconnect lets a client that lost its network for a moment resume the SAME connection: the
    ///     connection id stays, <c>OnDisconnectedAsync</c> / <c>OnConnectedAsync</c> do not run, and both sides replay
    ///     what the other missed from a buffer. It only happens for a client that asks for it
    ///     (<c>G9DtClientConnectionOptions.UseStatefulReconnect</c>, or <c>WithStatefulReconnect()</c> on a raw
    ///     builder); every other client of the endpoint is unaffected. The server keeps a buffer per such connection —
    ///     size it with <see cref="AddG9SignalRSuperNetCoreStatefulReconnect"/>.
    /// </remarks>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="routePattern">The route the hub is mapped at.</param>
    /// <param name="allowStatefulReconnects">Sets <see cref="HttpConnectionDispatcherOptions.AllowStatefulReconnects"/>.</param>
    /// <param name="configureHub">An optional callback to customize the endpoint; it runs last, so it can still override the flag.</param>
    public static IEndpointConventionBuilder AddSignalRSuperNetCoreServerHub<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app,
        string routePattern,
        bool allowStatefulReconnects,
        Action<HttpConnectionDispatcherOptions>? configureHub = null)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        return app.AddSignalRSuperNetCoreServerHub<TTargetClass, TClientSideMethodsInterface>(routePattern, options =>
        {
            options.AllowStatefulReconnects = allowStatefulReconnects;
            configureHub?.Invoke(options);
        });
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
    ///     Maps a JWT-protected SignalR hub like the overload without <paramref name="allowStatefulReconnects"/>, and
    ///     says whether the PROTECTED hub's endpoint accepts SignalR's stateful reconnect (see
    ///     <see cref="AddSignalRSuperNetCoreServerHub{TTargetClass,TClientSideMethodsInterface}(IEndpointRouteBuilder,string,bool,Action{HttpConnectionDispatcherOptions})"/>).
    ///     The auth route is a connection that lives for one call, so the flag is not applied to it.
    /// </summary>
    public static void AddSignalRSuperNetCoreJwtHub<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app,
        string hubRoutePattern,
        string authRoutePattern,
        Func<object, Hub, Task<(G9JWTokenFactory, object?)>> authenticate,
        bool allowStatefulReconnects,
        Action<HttpConnectionDispatcherOptions>? configureHub = null,
        Action<HttpConnectionDispatcherOptions>? configureAuthHub = null)
        where TTargetClass : G9AHubBaseWithJWTAuth<TTargetClass, TClientSideMethodsInterface>
        where TClientSideMethodsInterface : class
    {
        app.AddSignalRSuperNetCoreJwtHub<TTargetClass, TClientSideMethodsInterface>(hubRoutePattern, authRoutePattern, authenticate,
            options =>
            {
                options.AllowStatefulReconnects = allowStatefulReconnects;
                configureHub?.Invoke(options);
            },
            configureAuthHub);
    }

    /// <summary>
    ///     Registers <typeparamref name="TMarker"/> once per service collection and reports whether this call did it.
    ///     (A process-wide flag used to skip the registrations for every collection after the first one.)
    /// </summary>
    private static bool TryMark<TMarker>(IServiceCollection services) where TMarker : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services)
            if (descriptor.ServiceType == typeof(TMarker)) return false;
        services.AddSingleton(new TMarker());
        return true;
    }

    private sealed class G9CCoreServicesMarker;

    private sealed class G9CJwtServicesMarker;

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
