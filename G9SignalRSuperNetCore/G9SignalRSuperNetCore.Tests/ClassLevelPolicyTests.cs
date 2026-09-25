using G9SignalRSuperNetCore.Server.Classes.Errors;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     Role, claim and telemetry attributes on a hub class apply to every method (they used to be read from the method
///     only, so a class-level role check protected nothing). A class role set and a method role set must both pass.
/// </summary>
public sealed class ClassLevelPolicyTests
{
    private const string Tenant = "claims=tenant:acme";

    [Fact]
    public async Task A_class_level_role_and_claim_guard_every_method()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var anonymous = await server.ConnectRawAsync(RoleGuardedHub.Route);
        await using var noClaim = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=staff");
        await using var otherTenant = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=staff&claims=tenant:other");
        await using var staff = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=staff&" + Tenant);

        await AssertRefusedAsync(() => anonymous.InvokeAsync<string>("Read"), G9CErrorCodes.RoleRequired);
        await AssertRefusedAsync(() => noClaim.InvokeAsync<string>("Read"), G9CErrorCodes.ClaimRequired);
        await AssertRefusedAsync(() => otherTenant.InvokeAsync<string>("Read"), G9CErrorCodes.ClaimRequired);
        Assert.Equal("read", await staff.InvokeAsync<string>("Read"));
    }

    [Fact]
    public async Task A_method_role_adds_to_the_class_role_instead_of_replacing_it()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);
        await using var editorOnly = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=editor&" + Tenant);
        await using var staffOnly = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=staff&" + Tenant);
        await using var both = await server.ConnectRawAsync(RoleGuardedHub.Route + "?roles=staff,editor&" + Tenant);

        await AssertRefusedAsync(() => editorOnly.InvokeAsync<string>("Edit"), G9CErrorCodes.RoleRequired);
        await AssertRefusedAsync(() => editorOnly.InvokeAsync<string>("Read"), G9CErrorCodes.RoleRequired);
        await AssertRefusedAsync(() => staffOnly.InvokeAsync<string>("Edit"), G9CErrorCodes.RoleRequired);
        Assert.Equal("edited", await both.InvokeAsync<string>("Edit"));
    }

    private static async Task AssertRefusedAsync(Func<Task> call, string code)
    {
        var refused = await Assert.ThrowsAsync<HubException>(call);
        Assert.Contains(code, refused.Message);
    }
}
