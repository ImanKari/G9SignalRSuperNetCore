using G9SignalRSuperNetCore.Server.Classes.Authorization;
using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>2.9 <c>[G9AttrRequirePermission]</c>: every method- and class-level permission must be granted by the handler.</summary>
public sealed class PermissionTests
{
    [Fact]
    public async Task A_granted_permission_lets_the_call_through()
    {
        await using var server = await StartWithHandlerAsync();
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route + "?perms=docs.read");

        Assert.Equal("doc", await connection.InvokeAsync<string>("ReadDoc"));
    }

    [Fact]
    public async Task A_missing_permission_is_refused_with_the_stable_code()
    {
        await using var server = await StartWithHandlerAsync();
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route + "?perms=other");

        await AssertRefusedAsync(() => connection.InvokeAsync<string>("ReadDoc"));
    }

    [Fact]
    public async Task Every_listed_permission_is_required()
    {
        await using var server = await StartWithHandlerAsync();
        await using var readOnly = await server.ConnectRawAsync(PolicyHub.Route + "?perms=docs.read");
        await using var readWrite = await server.ConnectRawAsync(PolicyHub.Route + "?perms=docs.read,docs.write");

        await AssertRefusedAsync(() => readOnly.InvokeAsync<string>("WriteDoc"));
        Assert.Equal("written", await readWrite.InvokeAsync<string>("WriteDoc"));
    }

    [Fact]
    public async Task A_class_level_permission_applies_to_every_method_and_adds_to_the_method_level_ones()
    {
        await using var server = await StartWithHandlerAsync();
        await using var none = await server.ConnectRawAsync(GuardedHub.Route);
        await using var entered = await server.ConnectRawAsync(GuardedHub.Route + "?perms=hub.enter");
        await using var writer = await server.ConnectRawAsync(GuardedHub.Route + "?perms=hub.enter,docs.write");
        await using var writeOnly = await server.ConnectRawAsync(GuardedHub.Route + "?perms=docs.write");

        await AssertRefusedAsync(() => none.InvokeAsync<string>("Enter"));
        Assert.Equal("in", await entered.InvokeAsync<string>("Enter"));
        await AssertRefusedAsync(() => entered.InvokeAsync<string>("Write"));
        Assert.Equal("written", await writer.InvokeAsync<string>("Write"));
        await AssertRefusedAsync(() => writeOnly.InvokeAsync<string>("Write"));
    }

    [Fact]
    public async Task Without_a_registered_handler_a_permission_is_never_granted()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var connection = await server.ConnectRawAsync(PolicyHub.Route + "?perms=docs.read");

        await AssertRefusedAsync(() => connection.InvokeAsync<string>("ReadDoc"));
        // Methods without the attribute are unaffected.
        Assert.Equal(3, await connection.InvokeAsync<int>("Traced", 3));
    }

    private static Task<TestServer> StartWithHandlerAsync() => TestServer.StartAsync(offerMessagePack: false,
        configureServices: services => services.AddScoped<IG9HubPermissionHandler, QueryPermissionHandler>());

    private static async Task AssertRefusedAsync(Func<Task> call)
    {
        var refused = await Assert.ThrowsAsync<HubException>(call);
        Assert.Contains(G9CErrorCodes.PermissionRequired, refused.Message);
    }
}
