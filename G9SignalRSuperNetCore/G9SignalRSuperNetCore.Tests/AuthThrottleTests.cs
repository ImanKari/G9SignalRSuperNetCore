using System.Threading.Channels;
using G9SignalRSuperNetCore.Client.Classes.DataTypes;
using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Server.Classes.Helper;
using G9SignalRSuperNetCore.Server.Classes.Hubs;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>2.9: the JWT authorize route is throttled per remote IP before the credential check runs.</summary>
public sealed class AuthThrottleTests
{
    [Fact]
    public async Task Authorize_is_refused_with_G9_RATE_LIMITED_after_the_burst_and_the_handler_no_longer_runs()
    {
        var handlerCalls = 0;
        await using var server = await StartAsync("/throttle-auth", () => Interlocked.Increment(ref handlerCalls),
            options =>
            {
                options.AuthorizeBurstPerIp = 3;
                options.AuthorizePerMinutePerIp = 1;
            });
        await using var auth = await ConnectAuthAsync(server, "/throttle-auth");

        for (var i = 0; i < 3; i++)
        {
            var result = await auth.AuthorizeAsync();
            Assert.False(result.IsAccepted);
            Assert.Equal("bad credentials", result.RejectionReason); // the handler answered
        }

        var throttled = await auth.AuthorizeAsync();
        Assert.False(throttled.IsAccepted);
        Assert.Equal(G9CErrorCodes.RateLimited, throttled.RejectionReason);
        Assert.Null(throttled.JWToken);
        Assert.Equal(3, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public async Task A_disabled_throttle_lets_every_call_reach_the_handler()
    {
        var handlerCalls = 0;
        await using var server = await StartAsync("/unthrottled-auth", () => Interlocked.Increment(ref handlerCalls),
            options => options.ThrottleEnabled = false);
        await using var auth = await ConnectAuthAsync(server, "/unthrottled-auth");

        for (var i = 0; i < 30; i++)
            Assert.Equal("bad credentials", (await auth.AuthorizeAsync()).RejectionReason);
        Assert.Equal(30, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public void The_overload_without_options_registers_the_throttle_switched_on_with_the_defaults()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSignalRSuperNetCoreJwt("/defaults", new TokenValidationParameters());
        using var provider = services.BuildServiceProvider();

        var throttle = provider.GetRequiredService<G9CAuthThrottle>();
        Assert.True(throttle.Enabled);
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<G9DtJwtAuthOptions>>().Value;
        Assert.Equal(60, options.AuthorizePerMinutePerIp);
        Assert.Equal(20, options.AuthorizeBurstPerIp);

        // The default burst is 20 calls per address; the 21st is refused, another address is not affected.
        for (var i = 0; i < 20; i++) Assert.True(throttle.TryAcquire("10.0.0.1"));
        Assert.False(throttle.TryAcquire("10.0.0.1"));
        Assert.True(throttle.TryAcquire("10.0.0.2"));
        Assert.True(throttle.TryAcquire(null)); // no known address: not throttled
    }

    private static Task<TestServer> StartAsync(string authRoute, Action onAuthorize, Action<G9DtJwtAuthOptions> configure) =>
        TestServer.StartAsync(offerMessagePack: false,
            configureServices: services => services.AddSignalRSuperNetCoreJwt(SecureTestHub.Route, new TokenValidationParameters(), configure),
            configureApp: app => app.AddSignalRSuperNetCoreJwtHub<SecureTestHub, ISecureTestClient>(SecureTestHub.Route, authRoute,
                (_, _) =>
                {
                    onAuthorize();
                    return Task.FromResult<(G9JWTokenFactory, object?)>((G9JWTokenFactory.RejectAuthorize("bad credentials"), null));
                }));

    private static async Task<AuthConnection> ConnectAuthAsync(TestServer server, string authRoute)
    {
        var results = Channel.CreateUnbounded<G9DtAuthorizeResult>();
        var connection = await server.ConnectRawAsync(authRoute);
        connection.On<G9DtAuthorizeResult>("AuthorizeResult", result => results.Writer.TryWrite(result));
        return new AuthConnection(connection, results.Reader);
    }

    private sealed class AuthConnection(HubConnection connection, ChannelReader<G9DtAuthorizeResult> results) : IAsyncDisposable
    {
        public async Task<G9DtAuthorizeResult> AuthorizeAsync()
        {
            await connection.SendAsync("Authorize", new { user = "someone", password = "wrong" });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await results.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
