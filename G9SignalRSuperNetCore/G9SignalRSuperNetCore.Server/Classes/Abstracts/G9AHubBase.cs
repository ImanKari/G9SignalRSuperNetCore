using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

/// <summary>
///     A base class for implementing SignalR Hubs with support for client-side method invocation
///     and customizable configuration.
/// </summary>
/// <typeparam name="TTargetClass">
///     The derived Hub class inheriting from <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}" />.
/// </typeparam>
/// <typeparam name="TClientSideMethodsInterface">
///     An interface that defines the client-side methods which can be called from the server.
/// </typeparam>
public abstract class G9AHubBase<TTargetClass, TClientSideMethodsInterface> : Hub<TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
    where TClientSideMethodsInterface : class
{
    #region Constructor

    /// <summary>
    ///     Initializes a new instance of the <see cref="G9AHubBase{TTargetClass,TClientSideMethodsInterface}" /> class.
    /// </summary>
    protected G9AHubBase()
    {
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Configures the <see cref="HttpConnectionDispatcherOptions" /> for the Hub.
    /// </summary>
    /// <param name="configureOptions">
    ///     An object of <see cref="HttpConnectionDispatcherOptions" /> used to configure dispatcher options.
    /// </param>
    /// <remarks>
    ///     This method can be overridden to customize the behavior of HTTP connections.
    /// </remarks>
    public virtual void ConfigureHub(HttpConnectionDispatcherOptions configureOptions)
    {
    }

    /// <summary>
    ///     Configures the provided <see cref="HubOptions" /> for the Hub.
    /// </summary>
    /// <param name="configure">
    ///     An <see cref="Action{HubOptions}" /> used to apply custom configurations to the Hub options.
    /// </param>
    /// <remarks>
    ///     Override this method to define Hub-specific options such as client timeouts or message size limits.
    /// </remarks>
    public virtual void ConfigureHubOption(HubOptions configure)
    {
    }

    /// <summary>
    ///     Specifies the route pattern for the SignalR Hub.
    /// </summary>
    /// <returns>
    ///     A <see cref="string" /> representing the route pattern for the Hub.
    /// </returns>
    /// <remarks>
    ///     This method must be implemented in the derived class to define a unique route for the Hub.
    /// </remarks>
    public abstract string RoutePattern();

    #endregion
}