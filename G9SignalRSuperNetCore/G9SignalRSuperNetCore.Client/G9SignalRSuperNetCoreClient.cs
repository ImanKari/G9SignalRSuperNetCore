using System.Reflection;
using Castle.DynamicProxy;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client;

/// <summary>
///     A base class for a SignalR client that connects to a server hub and listens for events dynamically.
/// </summary>
/// <typeparam name="TTargetClass">
///     The type of the target class inheriting from this client.
/// </typeparam>
/// <typeparam name="TServerHubMethods">
///     An interface that defines the methods which can be invoked on the server.
/// </typeparam>
/// <typeparam name="TClientListenerMethods">
///     An interface that defines the methods which can be invoked by the server.
/// </typeparam>
public abstract class G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TTargetClass : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>,
    TClientListenerMethods
    where TServerHubMethods : class
{
    #region Fields And Properties

    /// <summary>
    ///     The underlying SignalR <see cref="HubConnection" /> used to communicate with the server.
    /// </summary>
    public readonly HubConnection Connection;

    private TServerHubMethods? _server;

    /// <summary>
    ///     Provides a proxy implementation of the server methods defined in <typeparamref name="TServerHubMethods" />.
    /// </summary>
    public TServerHubMethods Server => _server ??= CreateServerProxy();

    #endregion

    #region Methods

    /// <summary>
    ///     Creates a proxy for invoking server-side hub methods.
    /// </summary>
    /// <returns>A proxy object implementing <typeparamref name="TServerHubMethods" />.</returns>
    private TServerHubMethods CreateServerProxy()
    {
        var proxy = new ProxyGenerator<TServerHubMethods>(this);
        return proxy.GetProxy();
    }

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="G9SignalRSuperNetCoreClient{TTargetClass,TServerHubMethods,TClientListenerMethods}" /> class.
    /// </summary>
    /// <param name="serverUrl">The URL of the SignalR hub server.</param>
    /// <param name="customConfigureBuilder">
    ///     An optional function to customize the <see cref="IHubConnectionBuilder" />.
    /// </param>
    /// <param name="configureHttpConnection">
    ///     An optional action to configure the HTTP connection options.
    /// </param>
    protected G9SignalRSuperNetCoreClient(string serverUrl,
        Func<IHubConnectionBuilder, IHubConnectionBuilder>? customConfigureBuilder = null,
        Action<HttpConnectionOptions>? configureHttpConnection = null)
    {
        var preCompile = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => { configureHttpConnection?.Invoke(options); })
            .WithAutomaticReconnect();

        preCompile = customConfigureBuilder?.Invoke(preCompile) ?? preCompile;

        Connection = preCompile.Build();
        Connection.ServerTimeout = TimeSpan.FromSeconds(60); // Wait for server response for 60 seconds

        // Automatically wire up all methods from the TClientListenerMethods interface
        RegisterClientMethods();
    }

    /// <summary>
    ///     Registers all methods from the <typeparamref name="TClientListenerMethods" /> interface
    ///     as handlers for server-to-client calls.
    /// </summary>
    private void RegisterClientMethods()
    {
        var listenerType = typeof(TClientListenerMethods);

        foreach (var method in listenerType.GetMethods())
        {
            var methodName = method.Name;
            var parameters = method.GetParameters();
            var returnType = method.ReturnType;

            if (parameters.Length > 8)
                throw new NotSupportedException("Methods with more than 8 parameters are not supported.");

            var targetMethod = GetType().GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (targetMethod == null)
                continue;

            if (IsStreamingMethod(returnType))
            {
                var returnElementType = returnType.GetGenericArguments().First();
                _ = HandleStreamingMethod(targetMethod, returnElementType);
            }
            else
            {
                Connection.On(methodName, parameters.Select(p => p.ParameterType).ToArray(),
                    args => InvokeTargetMethod(targetMethod, args));
            }
        }
    }

    /// <summary>
    ///     Checks if the return type is a streaming type, specifically <see cref="IAsyncEnumerable{T}" />.
    /// </summary>
    /// <param name="returnType">The return type to check.</param>
    /// <returns>True if the return type is a streaming type; otherwise, false.</returns>
    private static bool IsStreamingMethod(Type returnType)
    {
        return returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);
    }

    /// <summary>
    ///     Handles streaming methods by reading data asynchronously from the server.
    /// </summary>
    /// <param name="targetMethod">The target method to invoke with the streamed data.</param>
    /// <param name="returnElementType">The type of the elements being streamed.</param>
    private async Task HandleStreamingMethod(MethodInfo targetMethod, Type returnElementType)
    {
        try
        {
            var channelReader =
                await Connection.StreamAsChannelCoreAsync(targetMethod.Name, returnElementType, [],
                    CancellationToken.None);

            while (await channelReader.WaitToReadAsync())
            while (channelReader.TryRead(out var item))
                targetMethod.Invoke(this, [item]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling stream for method {targetMethod.Name}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Invokes a target method dynamically with the specified arguments.
    /// </summary>
    /// <param name="method">The method to invoke.</param>
    /// <param name="args">The arguments to pass to the method.</param>
    /// <returns>A <see cref="Task" /> representing the asynchronous operation.</returns>
    private Task InvokeTargetMethod(MethodInfo method, object?[]? args)
    {
        var result = method.Invoke(this, args);
        return result as Task ?? Task.CompletedTask;
    }

    /// <summary>
    ///     Connects to the SignalR server asynchronously.
    /// </summary>
    public async Task ConnectAsync()
    {
        await Connection.StartAsync();
        Console.WriteLine("Connected to the server.");
    }

    /// <summary>
    ///     Disconnects from the SignalR server asynchronously.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await Connection.StopAsync();
        Console.WriteLine("Disconnected from the server.");
    }

    #endregion

    #region Proxy Part

    /// <summary>
    ///     A generator for creating proxy instances of the server methods.
    /// </summary>
    /// <typeparam name="TInterface">The server interface type.</typeparam>
    private class ProxyGenerator<TInterface>(
        G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods> client)
        where TInterface : class
    {
        /// <summary>
        ///     Creates a proxy implementation for the server interface.
        /// </summary>
        /// <returns>An instance of <typeparamref name="TInterface" />.</returns>
        public TInterface GetProxy()
        {
            var generator = new ProxyGenerator();
            var interceptor = new ServerMethodInterceptor(client.Connection);
            return generator.CreateInterfaceProxyWithoutTarget<TInterface>(interceptor);
        }
    }

    /// <summary>
    ///     Intercepts method calls to the server and sends them via the SignalR connection.
    /// </summary>
    private class ServerMethodInterceptor(HubConnection connection) : IInterceptor
    {
        /// <summary>
        ///     Intercepts and processes the method invocation.
        /// </summary>
        /// <param name="invocation">The intercepted method invocation.</param>
        public void Intercept(IInvocation invocation)
        {
            var methodName = invocation.Method.Name;
            var arguments = invocation.Arguments;

            var task = connection.SendCoreAsync(methodName, arguments);
            invocation.ReturnValue = task;
        }
    }

    #endregion
}