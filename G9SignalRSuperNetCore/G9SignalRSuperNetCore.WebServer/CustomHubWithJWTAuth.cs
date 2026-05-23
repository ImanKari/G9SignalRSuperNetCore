using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.WebServer;

/// <summary>
///     Sample JWT-protected hub without session state.
/// </summary>
public class CustomHubWithJWTAuth : G9AHubBaseWithJWTAuth<CustomHubWithJWTAuth, CustomClientInterface>
{
    private const string JwtSecretKey =
        "b32857808d7045c6adf38bb4ca6f6fb91798f8328da3f7f76c969b6ecb87b6407a306c28b0bd443aed9ec56b63311a4299594ebad7a4d10baf15c52f3b035bc59c1a72df7d4583c64b784ae928f009c07c7129f4b8c6f01113161d94b8da4bdf744acc45386126a54e02b41b538398dfdedfc56cb438e00da28d8bce1222322328e60cf24aca7002626cab0dd1fa830ca76238c759871e468f4609dc9a9d774c3bec7db1136560dd14625ab30c49ae0b761895c153beb5ae4243d156957327f5597125bbde7518e47874d83f1a20455ff82b6d3142f579346ad9fd5414fad6680d35b5ba230c6b6f1faca8a2a52a7a5580e8be3e06af8006d973173d7b8ab699";

    private const string Issuer = "G9TM";
    private const string Audience = "G9TM";

    private static readonly G9JWTokenFactory TokenTemplate =
        G9JWTokenFactory.GenerateJWTToken(
            JwtSecretKey, Issuer, Audience, DateTime.UtcNow.AddDays(3),
            G9ESecurityAlgorithms.HmacSha256);

    public override string RoutePattern() => "/SecureHub";
    public override string AuthAndGetJWTRoutePattern() => "/AuthHub";

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(
        object authorizeData, Hub accessToUnauthorizedVirtualHub)
    {
        if (authorizeData?.ToString() ==
            "jg93w4t9swhuwgvosedrgf029ptg2qw38r0dfgw239p84521039r8hwaqfy8o923519723rgfw923w4ty#$&Y#$WUYHW#$&YW@#$TG@#$^#$")
        {
            var token = G9JWTokenFactory.GenerateJWTToken(
                JwtSecretKey, "Meti", "admin", Issuer, Audience, DateTime.UtcNow.AddDays(3));
            return Task.FromResult<(G9JWTokenFactory, object?)>((token, "This Is Awesome"));
        }

        return Task.FromResult<(G9JWTokenFactory, object?)>(
            (G9JWTokenFactory.RejectAuthorize("Incorrect Authorize Data!"), "Noooo!"));
    }

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub()
        => TokenTemplate.ValidationParameters!;

    public Task Login(string userName, string password) => Clients.Caller.LoginResult(true);

    public Task Replay(string message)
    {
        var role = Context.User?.IsInRole("admin") ?? false;
        Console.WriteLine($"Replay from {Context.ConnectionId} (admin={role})");
        return Clients.Caller.Replay(message);
    }
}
