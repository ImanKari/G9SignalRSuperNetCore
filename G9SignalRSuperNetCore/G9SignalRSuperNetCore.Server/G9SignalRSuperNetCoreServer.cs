using System.Security.Claims;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Server;

public static class G9SignalRSuperNetCoreServer
{
    public static void AddSignalRSuperNetCoreServerService<TTargetClass, TClientSideMethodsInterface>
        (this IServiceCollection services, Func<HubConnectionContext, string?>? userIdentifier = null)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
        where TClientSideMethodsInterface : class
    {
        var targetClass = new TTargetClass();

        services.AddSingleton<IUserIdProvider, G9CUserIdProvider>(_ => new G9CUserIdProvider(userIdentifier));

        services.AddSignalR(option =>
        {
            option.MaximumReceiveMessageSize = 200 * 1024 * 1024; // nMB * ... * ..
            option.KeepAliveInterval = TimeSpan.FromSeconds(10); // Server sends ping every 10 seconds
            option.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Wait for 60 seconds for client response

            targetClass.ConfigureHubOption(option);
        });
    }

    public static void AddSignalRSuperNetCoreServerHub<TTargetClass, TClientSideMethodsInterface>(
        this IEndpointRouteBuilder app)
        where TTargetClass : G9AHubBase<TTargetClass, TClientSideMethodsInterface>, new()
        where TClientSideMethodsInterface : class
    {
        var targetClass = new TTargetClass();


        app.MapHub<TTargetClass>(targetClass.RoutePattern(), targetClass.ConfigureHub);
    }

    public class G9CUserIdProvider : IUserIdProvider
    {
        private readonly Func<HubConnectionContext, string?>? _userIdentifier;

        public G9CUserIdProvider(Func<HubConnectionContext, string?>? userIdentifier)
        {
            _userIdentifier = userIdentifier;
        }

        public string? GetUserId(HubConnectionContext connection)
        {
            if (_userIdentifier != null) return _userIdentifier(connection);

            if (connection.User?.FindFirst(ClaimTypes.NameIdentifier) is { } nameIdentifier)
                return nameIdentifier.Value;

            return connection.UserIdentifier;
        }
    }
}