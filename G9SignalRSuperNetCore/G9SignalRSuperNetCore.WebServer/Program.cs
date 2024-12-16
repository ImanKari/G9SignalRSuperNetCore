using G9SignalRSuperNetCore.Server;

namespace G9SignalRSuperNetCore.WebServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = 200 * 1024 * 1024; // 10 MB
                options.KeepAliveInterval = TimeSpan.FromSeconds(10); // Server sends ping every 10 seconds
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Wait for 60 seconds for client response

            });

#if DEBUG
            builder.Logging.AddConsole();
#endif

            var app = builder.Build();

            app.MapGet("/", () => "Hello World!");

            app.MapHub<G9SignalRSuperNetCoreServer>("/serverHub");

            app.Run();
        }
    }
}
