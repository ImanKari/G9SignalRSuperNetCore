using G9SignalRSuperNetCore.Server.Classes.DataTypes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

public abstract class G9AHubBase<TTargetClass, TClientSideMethodsInterface> : Hub<TClientSideMethodsInterface>
    where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
    where TClientSideMethodsInterface : class
{

    #region Fields And Properties

    internal List<G9DtClientSideMethod> ClientSideMethods { get; set; } = [];

    #endregion

    #region Constructor

    protected G9AHubBase()
    {
        
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Method to configure dispatcher options
    /// </summary>
    /// <param name="configureOptions">An object to configure dispatcher options.</param>
    public virtual void ConfigureHub(HttpConnectionDispatcherOptions configureOptions)
    {
    }

    /// <summary>
    ///     Method to configure the provided <see cref="HubOptions" />.
    /// </summary>
    /// <param name="configure">An <see cref="Action{HubOptions}" /> to configure the provided <see cref="HubOptions" />.</param>
    public virtual void ConfigureHubOption(HubOptions configure)
    {
    }

    /// <summary>
    /// Method to specify the route pattern.
    /// </summary>
    /// <returns>The route pattern.</returns>
    public abstract string RoutePattern();

    #endregion
}
