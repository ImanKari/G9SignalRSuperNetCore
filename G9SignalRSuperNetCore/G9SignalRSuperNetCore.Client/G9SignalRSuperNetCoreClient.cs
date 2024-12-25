using System.Linq.Expressions;
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
    where TTargetClass : G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods>
    where TServerHubMethods : class
    where TClientListenerMethods : class
{
    #region Fields And Properties

    /// <summary>
    ///     The underlying SignalR <see cref="HubConnection" /> used to communicate with the server.
    /// </summary>
    public HubConnection Connection { protected internal set; get; } = null!;

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
        PrepareConnection(serverUrl, customConfigureBuilder, configureHttpConnection);
    }

    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="G9SignalRSuperNetCoreClient{TTargetClass,TServerHubMethods,TClientListenerMethods}" /> class.
    /// </summary>
    protected internal G9SignalRSuperNetCoreClient()
    {
    }

    /// <summary>
    ///     Method to prepare server
    /// </summary>
    /// <param name="serverUrl">The URL of the SignalR hub server.</param>
    /// <param name="customConfigureBuilder">
    ///     An optional function to customize the <see cref="IHubConnectionBuilder" />.
    /// </param>
    /// <param name="configureHttpConnection">
    ///     An optional action to configure the HTTP connection options.
    /// </param>
    protected internal void PrepareConnection(string serverUrl,
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

    #region Server Methods Proxy

    /// <summary>
    ///     A generator for creating proxy instances of the server methods.
    /// </summary>
    /// <typeparam name="TInterface">The server interface type.</typeparam>
    private class ProxyGenerator<TInterface>
        where TInterface : class
    {
        private readonly G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods> _client;

        public ProxyGenerator(
            G9SignalRSuperNetCoreClient<TTargetClass, TServerHubMethods, TClientListenerMethods> client)
        {
            _client = client;
        }

        /// <summary>
        ///     Creates a proxy implementation for the server interface.
        /// </summary>
        /// <returns>An instance of <typeparamref name="TInterface" />.</returns>
        public TInterface GetProxy()
        {
            var generator = new ProxyGenerator();
            var interceptor = new ServerMethodInterceptor(_client.Connection);
            return generator.CreateInterfaceProxyWithoutTarget<TInterface>(interceptor);
        }
    }

    /// <summary>
    ///     Intercepts method calls to the server and sends them via the SignalR connection.
    /// </summary>
    private class ServerMethodInterceptor : IInterceptor
    {
        private readonly HubConnection _connection;

        public ServerMethodInterceptor(HubConnection connection)
        {
            _connection = connection;
        }

        /// <summary>
        ///     Intercepts and processes the method invocation.
        /// </summary>
        /// <param name="invocation">The intercepted method invocation.</param>
        public void Intercept(IInvocation invocation)
        {
            var methodName = invocation.Method.Name;
            var arguments = invocation.Arguments;

            var task = _connection.SendCoreAsync(methodName, arguments);
            invocation.ReturnValue = task;
        }
    }

    #endregion

    #region Assign Listener Events Methods

    /// <summary>
    ///     Registers a SignalR hub event listener for the specified method and its implementation.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type representing the method signature.</typeparam>
    /// <param name="methodSelector">
    ///     An expression that selects a method from the <typeparamref name="TClientListenerMethods" /> interface.
    /// </param>
    /// <param name="implementation">The delegate implementation to handle the event.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="methodSelector" /> expression is invalid or does not represent a compatible method.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the delegate's method signature cannot be retrieved.
    /// </exception>
    private void AssignListenerEventDelegateHandler<TDelegate>(
        Expression<Func<TClientListenerMethods, TDelegate>> methodSelector,
        TDelegate implementation)
        where TDelegate : Delegate
    {
        // Extract method name and parameter types
        var (methodName, parameterTypes) = ExtractMethodDetails(methodSelector);

        // Register the SignalR hub event
        Connection.On(methodName, parameterTypes, args =>
        {
            // Invoke the implementation with the provided arguments
            return InvokeImplementation(implementation, args);
        });
    }

    /// <summary>
    ///     Extracts method name and parameter types from a method selector expression that targets interface methods.
    /// </summary>
    /// <typeparam name="TDelegate">The type of delegate that matches the selected method's signature.</typeparam>
    /// <param name="methodSelector">
    ///     An expression that selects a method from the TClientListenerMethods interface.
    ///     For example: s => s.MethodName
    /// </param>
    /// <returns>
    ///     A tuple containing:
    ///     - methodName: The name of the selected method from the interface
    ///     - parameterTypes: An array of Type objects representing the parameter types of the method
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when:
    ///     - No matching method is found in the interface
    ///     - The method selector expression is invalid
    ///     - The delegate signature doesn't match any interface method
    /// </exception>
    /// <remarks>
    ///     This method uses reflection to:
    ///     1. Find a method in TClientListenerMethods interface that matches the delegate's parameter types
    ///     2. Extract the parameter types from the delegate's Invoke method
    ///     The matching is done by comparing parameter types and counts between the interface method
    ///     and the delegate to ensure type safety.
    /// </remarks>
    private (string methodName, Type[] parameterTypes) ExtractMethodDetails<TDelegate>(
        Expression<Func<TClientListenerMethods, TDelegate>> methodSelector)
    {
        // Get the method info from the interface type by finding a method whose parameters match the delegate
        var methodName = typeof(TClientListenerMethods)
            .GetMethods()
            .FirstOrDefault(m =>
            {
                var delegateType = typeof(TDelegate);
                var delegateParams = delegateType.GetMethod("Invoke")?.GetParameters();
                var methodParams = m.GetParameters();

                // Check if parameter counts match and all parameter types are identical
                return delegateParams != null &&
                       methodParams.Length == delegateParams.Length &&
                       methodParams.Zip(delegateParams, (m, d) => m.ParameterType == d.ParameterType).All(x => x);
            })
            ?.Name ?? throw new ArgumentException("Could not find matching method.", nameof(methodSelector));

        // Extract parameter types from the delegate's Invoke method
        var parameterTypes = typeof(TDelegate)
            .GetMethod("Invoke")
            ?.GetParameters()
            .Select(p => p.ParameterType)
            .ToArray() ?? Array.Empty<Type>();

        return (methodName, parameterTypes);
    }

    /// <summary>
    ///     Invokes the provided delegate implementation dynamically with the specified arguments.
    /// </summary>
    /// <param name="implementation">The delegate implementation to invoke.</param>
    /// <param name="args">The arguments to pass to the delegate.</param>
    /// <returns>
    ///     A <see cref="Task" /> representing the asynchronous operation of the delegate.
    /// </returns>
    /// <exception cref="TargetInvocationException">
    ///     Thrown if the invoked method throws an exception.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown if the arguments do not match the delegate's expected signature.
    /// </exception>
    private Task InvokeImplementation(Delegate implementation, object?[] args)
    {
        try
        {
            var result = implementation.DynamicInvoke(args);

            // Handle Task return type
            if (result is Task taskResult)
                return taskResult;

            return Task.CompletedTask;
        }
        catch (TargetInvocationException tie)
        {
            throw new TargetInvocationException(
                $"Error invoking implementation for delegate '{implementation.Method.Name}': {tie.InnerException?.Message}",
                tie.InnerException);
        }
        catch (ArgumentException ae)
        {
            throw new ArgumentException(
                $"Error invoking delegate '{implementation.Method.Name}' due to invalid arguments: {ae.Message}", ae);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unexpected error invoking delegate '{implementation.Method.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Assigns an event listener for a SignalR hub method with a specified implementation.
    ///     This method registers the selected method on the SignalR hub and ensures the provided
    ///     implementation matches the signature of the selected method.
    /// </summary>
    /// <remarks>
    ///     Example usage:
    ///     <code>
    /// var client = new G9CCustomHubClient("https://localhost:7159");
    /// client.AssignListenerEvent(
    ///     s => s.Replay, (string param) =>
    ///     {
    ///         Console.WriteLine(param);
    ///         return Task.CompletedTask;
    ///     });
    /// </code>
    ///     Overloads are provided to support methods with up to 8 parameters. For each overload, the implementation delegate
    ///     must have the same signature
    ///     as the selected method in terms of both parameter types and return type.
    /// </remarks>
    /// <param name="methodSelector">
    ///     An expression that specifies the method in the <typeparamref name="TClientListenerMethods" /> interface
    ///     to register as an event listener.
    /// </param>
    /// <param name="implementation">
    ///     A delegate representing the implementation for handling the event.
    ///     The implementation must match the selected method's signature exactly, including parameter types and the return
    ///     type.
    /// </param>
    /// <typeparam name="TClientListenerMethods">
    ///     The interface type defining the SignalR client listener methods.
    /// </typeparam>
    /// <exception cref="ArgumentException">
    ///     Thrown if the method selector expression is invalid or does not represent a compatible method.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the delegate's method signature cannot be retrieved.
    /// </exception>
    public void AssignListenerEvent(
        Expression<Func<TClientListenerMethods, Func<Task>>> methodSelector,
        Func<Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1>(
        Expression<Func<TClientListenerMethods, Func<T1, Task>>> methodSelector,
        Func<T1, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, Task>>> methodSelector,
        Func<T1, T2, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, Task>>> methodSelector,
        Func<T1, T2, T3, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3, T4>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, T4, Task>>> methodSelector,
        Func<T1, T2, T3, T4, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3, T4, T5>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, T4, T5, Task>>> methodSelector,
        Func<T1, T2, T3, T4, T5, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3, T4, T5, T6>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, T4, T5, T6, Task>>> methodSelector,
        Func<T1, T2, T3, T4, T5, T6, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3, T4, T5, T6, T7>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, T4, T5, T6, T7, Task>>> methodSelector,
        Func<T1, T2, T3, T4, T5, T6, T7, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    /// <inheritdoc cref="AssignListenerEvent" />
    public void AssignListenerEvent<T1, T2, T3, T4, T5, T6, T7, T8>(
        Expression<Func<TClientListenerMethods, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task>>> methodSelector,
        Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> implementation)
    {
        AssignListenerEventDelegateHandler(methodSelector, implementation);
    }

    #endregion
}