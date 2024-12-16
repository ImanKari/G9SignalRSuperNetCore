using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Server.Clasess.Extensions;

public static class G9CSignalRServerExtensions
{
    public static void AddSignalRServer(this IServiceCollection services)
    {
        services.AddSignalR();
    }

    public static void MapSignalRServer(this IApplicationBuilder app)
    {
        //app.UseEndpointRouting(endpoints =>
        //{
        //    endpoints.MapHub<ChatHub>("/chatHub");
        //});
    }
}