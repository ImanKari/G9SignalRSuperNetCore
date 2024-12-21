using System.Security.Claims;
using System.Text;
using G9SignalRSuperNetCore.Server;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.WebServer;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // SignalR Super Net Core Server Service
        builder.Services.AddSignalRSuperNetCoreServerService<CustomHubWithJWTAuthAndSession, CustomClientInterface>();

#if DEBUG
        builder.Logging.AddConsole();
#endif

        // ----------------------------- Authentication -----------------------------
        //builder.Services.AddAuthentication()
        //    .AddCookie(option =>
        //    {
        //        option.LoginPath= "/Login";
        //        option.LogoutPath = "/Logout";
        //        option.AccessDeniedPath = "/AccessDenied";
        //        option.ExpireTimeSpan = TimeSpan.FromDays(3);
        //    })
        //    .AddJwtBearer(option =>
        //    {
        //        option.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
        //        {
        //            ValidIssuer = "ByteOrbitTeam",
        //            ValidAudience = "ByteOrbitTeam",
        //            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("asd")),
        //            ValidateIssuer = true,
        //            ValidateAudience = true,
        //            ValidateLifetime = true,
        //            ValidateIssuerSigningKey = true,
        //        };
        //    });
        //builder.Services.AddAuthorization(option =>
        //{
        //    option.AddPolicy("+18", policyBuilder =>
        //    {
        //        policyBuilder.RequireClaim("BiggerThan18", "true");
        //    });
        //});

        var app = builder.Build();

        app.MapGet("/", () => "Hello World!");

        //app.MapHub<G9SignalRSuperNetCoreServerHub>("/aa", options => { });
        //app.AddSignalRSuperNetCoreServerHub<CustomHub, CustomClientInterface>();
        app.AddSignalRSuperNetCoreServerHub<CustomHubWithJWTAuthAndSession, CustomClientInterface>();
        //app.AddSignalRSuperNetCoreServerHub<CustomHubWithSession, CustomClientInterface>();

        //Claim a1 = new Claim(ClaimTypes.NameIdentifier, "Identifier");
        //Claim a2 = new Claim(ClaimTypes.Email, "Iman.Kari@Live.Com");

        //Claim a3 = new Claim(ClaimTypes.Role, "Customer");

        //ClaimsIdentity userIdentity = new ClaimsIdentity([a1, a2, a3], "UserIdentity");

        //Claim a4 = new Claim(ClaimTypes.Role, "Manager");

        //ClaimsIdentity managerIdentity = new ClaimsIdentity([a1, a2, a4], "ManagerIdentity");

        //Claim a5 = new Claim(ClaimTypes.Role, "Admin");

        //ClaimsIdentity adminIdentity = new ClaimsIdentity([a1, a2, a5], "AdminIdentity");

        //ClaimsPrincipal principal = new ClaimsPrincipal([userIdentity, managerIdentity, adminIdentity]);

        

        //app.MapControllers();

        app.Run();
    }
}
