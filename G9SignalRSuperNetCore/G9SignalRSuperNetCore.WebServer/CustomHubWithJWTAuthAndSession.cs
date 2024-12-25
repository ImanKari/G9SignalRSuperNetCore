using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.WebServer;

public class CustomHubWithJWTAuthAndSession : G9AHubBaseWithSessionAndJWTAuth<CustomHubWithJWTAuthAndSession, CustomClientInterface, CustomHubSession>
{

    private readonly ILogger<CustomHubWithJWTAuthAndSession> _logger;

    public CustomHubWithJWTAuthAndSession(ILogger<CustomHubWithJWTAuthAndSession> logger)
    {
        _logger = logger;
    }

    public override string RoutePattern()
    {
        return "/SecureHub";
    }

    public override string AuthAndGetJWTRoutePattern()
    {
        return "/AuthHub";
    }

    private const string JWTokenSecretKey = "b32857808d7045c6adf38bb4ca6f6fb91798f8328da3f7f76c969b6ecb87b6407a306c28b0bd443aed9ec56b63311a4299594ebad7a4d10baf15c52f3b035bc59c1a72df7d4583c64b784ae928f009c07c7129f4b8c6f01113161d94b8da4bdf744acc45386126a54e02b41b538398dfdedfc56cb438e00da28d8bce1222322328e60cf24aca7002626cab0dd1fa830ca76238c759871e468f4609dc9a9d774c3bec7db1136560dd14625ab30c49ae0b761895c153beb5ae4243d156957327f5597125bbde7518e47874d83f1a20455ff82b6d3142f579346ad9fd5414fad6680d35b5ba230c6b6f1faca8a2a52a7a5580e8be3e06af8006d973173d7b8ab699";

    private static readonly G9JWTokenFactory jwToken =
        G9JWTokenFactory.GenerateJWTToken(JWTokenSecretKey, "Meti", "admin", "G9TM", "G9TM", DateTime.Now.AddDays(3), G9ESecurityAlgorithms.HmacSha256);

    public override Task<G9JWTokenFactory> AuthenticateAndGenerateJwtTokenAsync(object authorizeData,
        Hub accessToUnauthorizedVirtualHub)
    {
        // caller Ip Address
        // accessToUnauthorizedVirtualHub.Context.GetHttpContext()?.Connection.RemoteIpAddress
        if (authorizeData.ToString() ==
            "jg93w4t9swhuwgvosedrgf029ptg2qw38r0dfgw239p84521039r8hwaqfy8o923519723rgfw923w4ty#$&Y#$WUYHW#$&YW@#$TG@#$^#$")
            return Task.FromResult(jwToken);

        return Task.FromResult(G9JWTokenFactory.RejectAuthorize("Incorrect Authorize Data!"));
    }

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub()
    {
        return jwToken.ValidationParameters!;
    }

    /// <summary>
    ///     Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    public async Task Login(string userName, string password)
    {
        await Clients.Caller.LoginResult(true);
    }

    /// <summary>
    ///     Replay
    /// </summary>
    /// <param name="message">Okay</param>
    public async Task Replay(string message)
    {
        var user = Context.User;
        Console.WriteLine(Context.ConnectionId);
        await Clients.Caller.Replay(message);
    }

}