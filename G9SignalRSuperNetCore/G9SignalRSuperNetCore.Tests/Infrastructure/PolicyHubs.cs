using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Authorization;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Tests.Infrastructure;

public interface IPolicyHubClient
{
    Task Notice(string text);
}

/// <summary>2.9 policies: rate-limit scopes, permissions, streaming telemetry and sampling.</summary>
public sealed class PolicyHub : G9AHubBase<PolicyHub, IPolicyHubClient>
{
    public const string Route = "/policy";

    /// <summary>Burst 3 and a refill of one call per 100 s: the fourth call inside a test is always refused.</summary>
    private const double Slow = 0.01;

    [RequiresDynamicCode("Test hub; SignalR Hub<T> requires dynamic code.")]
    public PolicyHub()
    {
    }

    public override string RoutePattern() => Route;

    [G9AttrRateLimit(Slow, 3)]
    public Task<int> PerConnection(int n) => Task.FromResult(n);

    [G9AttrRateLimit(Slow, 3, Scope = G9ERateLimitScope.User)]
    public Task<int> PerUser(int n) => Task.FromResult(n);

    [G9AttrRateLimit(Slow, 3, Scope = G9ERateLimitScope.Ip)]
    public Task<int> PerIp(int n) => Task.FromResult(n);

    [G9AttrRequirePermission("docs.read")]
    public Task<string> ReadDoc() => Task.FromResult("doc");

    [G9AttrRequirePermission("docs.read")]
    [G9AttrRequirePermission("docs.write")]
    public Task<string> WriteDoc() => Task.FromResult("written");

    [G9AttrTelemetry("PolicyHub.Count")]
    public async IAsyncEnumerable<int> Count(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }

    /// <summary>A ChannelReader stream (not supported by the typed client generator, hence excluded from it).</summary>
    [G9AttrTelemetry("PolicyHub.Channel")]
    [G9AttrExcludeFromClientGeneration]
    public ChannelReader<int> Channel(int count)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<int>();
        for (var i = 1; i <= count; i++) channel.Writer.TryWrite(i);
        channel.Writer.Complete();
        return channel.Reader;
    }

    [G9AttrTelemetry("PolicyHub.Traced")]
    public Task<int> Traced(int n) => Task.FromResult(n);

    [G9AttrTelemetry("PolicyHub.Unsampled", SampleRate = 0)]
    public Task<int> Unsampled(int n) => Task.FromResult(n);
}

/// <summary>A hub whose every method needs "hub.enter" (class level); <see cref="Write"/> needs "docs.write" too.</summary>
[G9AttrRequirePermission("hub.enter")]
public sealed class GuardedHub : G9AHubBase<GuardedHub, IPolicyHubClient>
{
    public const string Route = "/guarded";

    [RequiresDynamicCode("Test hub; SignalR Hub<T> requires dynamic code.")]
    public GuardedHub()
    {
    }

    public override string RoutePattern() => Route;

    public Task<string> Enter() => Task.FromResult("in");

    [G9AttrRequirePermission("docs.write")]
    public Task<string> Write() => Task.FromResult("written");
}

/// <summary>
///     Class-level role and claim (every method) plus a method adding its own role: both role gates must pass
///     (the test server builds the user from <c>?roles=</c> and <c>?claims=type:value</c>).
/// </summary>
[G9AttrRequireRole("staff")]
[G9AttrRequireClaim("tenant", "acme")]
public sealed class RoleGuardedHub : G9AHubBase<RoleGuardedHub, IPolicyHubClient>
{
    public const string Route = "/roles";

    [RequiresDynamicCode("Test hub; SignalR Hub<T> requires dynamic code.")]
    public RoleGuardedHub()
    {
    }

    public override string RoutePattern() => Route;

    public Task<string> Read() => Task.FromResult("read");

    [G9AttrRequireRole("editor")]
    public Task<string> Edit() => Task.FromResult("edited");
}

/// <summary>Grants the permissions listed in the connection query string: <c>?perms=a,b</c>.</summary>
public sealed class QueryPermissionHandler : IG9HubPermissionHandler
{
    public ValueTask<bool> IsAllowedAsync(HubInvocationContext context, string permission)
    {
        var granted = context.Context.GetHttpContext()?.Request.Query["perms"].ToString() ?? string.Empty;
        return ValueTask.FromResult(granted.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(permission, StringComparer.Ordinal));
    }
}

public interface ISecureTestClient
{
    Task Notice(string text);
}

/// <summary>A JWT hub, so the auth route (<c>G9GetJwtHub</c>) can be mapped by the library helper.</summary>
public sealed class SecureTestHub : G9AHubBaseWithJWTAuth<SecureTestHub, ISecureTestClient>
{
    public const string Route = "/secure";
    public const string AuthRoute = "/secure-auth";

    [RequiresDynamicCode("Test hub; SignalR Hub<T> requires dynamic code.")]
    public SecureTestHub()
    {
    }

    public override string RoutePattern() => Route;

    public override string AuthAndGetJWTRoutePattern() => AuthRoute;

    public override Task<(G9JWTokenFactory, object?)> AuthenticateAndGenerateJwtTokenAsync(object authorizeData, Hub accessToUnauthorizedVirtualHub) =>
        Task.FromResult<(G9JWTokenFactory, object?)>((G9JWTokenFactory.RejectAuthorize("bad credentials"), null));

    public override TokenValidationParameters GetAuthorizeTokenValidationForHub() => new();

    public Task<string> Hello() => Task.FromResult("hello");
}
