using G9SignalRSuperNetCore.Server.MessagePack;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     What awaiting a generated method means. Up to 2.6.0 a method returning <c>Task</c> was sent and the
///     task completed as soon as the message was written, so <c>await</c> returned before the server had
///     run anything and a server-side failure could not be seen at all. From 2.7.0 it is an acknowledged
///     invocation unless the hub method says otherwise with <c>[G9AttrOneWay]</c>.
/// </summary>
public sealed class CallSemanticsTests
{
    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task Awaiting_a_no_result_method_waits_for_the_server_to_finish_it(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = await server.ConnectAsync(protocol);
        var before = Volatile.Read(ref TestHub.SlowWorkCompleted);

        await client.Server.SlowWork(300);

        // No polling and no delay: if the await returned before the server finished, this reads the old value.
        Assert.Equal(before + 1, Volatile.Read(ref TestHub.SlowWorkCompleted));
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_server_side_failure_in_a_no_result_method_reaches_the_caller(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = await server.ConnectAsync(protocol);

        var failure = await Assert.ThrowsAsync<HubException>(() => client.Server.AlwaysFails());

        Assert.Contains("the server refused", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_method_marked_one_way_returns_without_waiting(TestProtocol protocol)
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = await server.ConnectAsync(protocol);
        var before = Volatile.Read(ref TestHub.SlowWorkCompleted);

        await client.Server.FireAndForget(3_000);

        // The point of the opt-out: the caller is back long before the server is done.
        Assert.Equal(before, Volatile.Read(ref TestHub.SlowWorkCompleted));
    }
}
