using System.Security.Cryptography;
using G9SignalRSuperNetCore.Client.FileUpload;
using G9SignalRSuperNetCore.Client.MessagePack;
using G9SignalRSuperNetCore.Server.Classes.FileUpload;
using G9SignalRSuperNetCore.Server.MessagePack;
using G9SignalRSuperNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using PolyType;
using ClientDto = G9SignalRSuperNetCore.Client.FileUpload;
using ServerDto = G9SignalRSuperNetCore.Server.Classes.FileUpload;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     The opt-in MessagePack hub protocol (<c>G9SignalRSuperNetCore.Client.MessagePack</c> /
///     <c>.Server.MessagePack</c>) against a real server that offers it next to JSON.
/// </summary>
public sealed class MessagePackProtocolTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Json_and_MessagePack_clients_of_one_server_get_the_same_answers_in_their_own_format()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        var transcripts = new Dictionary<TestProtocol, string>();
        foreach (var protocol in new[] { TestProtocol.Json, TestProtocol.MessagePack })
        {
            await using var client = await server.ConnectAsync(protocol);
            var format = await client.Server.TransferFormat();
            Assert.Equal(protocol == TestProtocol.MessagePack ? "Binary" : "Text", format);

            var lines = new List<string>();
            var sent = TestReading.Create(6);
            lines.Add("echo " + (await client.Server.Echo(sent)).Describe());
            lines.AddRange((await client.Server.Batch(4)).Select(r => "batch " + r.Describe()));
            await foreach (var reading in client.Server.Readings(5, CancellationToken.None)) lines.Add("stream " + reading.Describe());
            lines.Add("sum " + await client.Server.Sum(Count(1, 100)));
            await client.Server.Poke(9);
            lines.Add("poked " + (await client.FirstPoke.WaitAsync(TimeSpan.FromSeconds(10))).Describe());

            Assert.Equal(sent.Describe(), lines[0]["echo ".Length..]);
            Assert.Equal("sum 5050", lines.Single(l => l.StartsWith("sum", StringComparison.Ordinal)));
            transcripts[protocol] = string.Join('\n', lines);
        }

        Assert.Equal(transcripts[TestProtocol.Json], transcripts[TestProtocol.MessagePack]);
    }

    [Theory]
    [InlineData(TestProtocol.Json)]
    [InlineData(TestProtocol.MessagePack)]
    public async Task A_file_goes_up_and_comes_back_intact_with_server_acknowledgements_over_either_protocol(TestProtocol protocol)
    {
        // TestShapes declares only the app's own types: over MessagePack every file-transfer type comes from the library's shapes.
        Assert.Null(TestShapes.GeneratedTypeShapeProvider.GetTypeShape(typeof(ClientDto.G9DtBeginUploadResult)));
        Assert.Null(TestShapes.GeneratedTypeShapeProvider.GetTypeShape(typeof(ClientDto.G9DtUploadProgress)));

        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        await using var client = await server.ConnectAsync(protocol);
        const int Size = 3 * 1024 * 1024 + 123;
        var source = WriteRandomFile(Size);
        try
        {
            // A second handler for the same callback, typed like G9CFileUploader's own: SignalR binds arguments with the FIRST
            // handler's types (the generated listener's), so both must name the same type.
            long seenByUploaderStyleHandler = 0;
            using var extra = client.Connection.On<ClientDto.G9DtUploadProgress>("UploadProgress",
                p => Interlocked.Exchange(ref seenByUploaderStyleHandler, Math.Max(p.BytesReceived, Interlocked.Read(ref seenByUploaderStyleHandler))));

            var uploaded = await new G9CFileUploader(client.Connection, new G9DtUploadClientOptions { ChunkSize = 256 * 1024 }).UploadAsync(source);

            Assert.Equal(ClientDto.G9EUploadStatus.Completed, uploaded.Status);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))), uploaded.Sha256, ignoreCase: true);
            // The server's final acknowledgement (G9DtUploadProgress) reaches the generated listener. Up to 2.5.3 it was bound to
            // the server DTO: the uploader's own handler failed its cast over JSON, and over MessagePack nothing arrived at all.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((client.Acknowledged < Size || Interlocked.Read(ref seenByUploaderStyleHandler) < Size) && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.Equal(Size, client.Acknowledged);
            Assert.Equal(Size, Interlocked.Read(ref seenByUploaderStyleHandler));

            var target = source + ".back";
            var downloaded = await new G9CFileDownloader(client.Connection, new G9DtDownloadClientOptions { ChunkSize = 128 * 1024 })
                .DownloadAsync(Path.GetFileName(uploaded.FinalPath!), target);

            Assert.Equal(ClientDto.G9EUploadStatus.Completed, downloaded.Status);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
            File.Delete(target);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task Binary_chunks_travel_as_bytes_over_MessagePack_but_as_base64_text_over_Json()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: true);
        const int Size = 2 * 1024 * 1024;
        var received = new Dictionary<TestProtocol, long>();
        foreach (var protocol in new[] { TestProtocol.Json, TestProtocol.MessagePack })
        {
            await using var client = await server.ConnectAsync(protocol);
            var source = WriteRandomFile(Size);
            try
            {
                server.Wire.Reset();
                var result = await new G9CFileUploader(client.Connection).UploadAsync(source, serverAckProgress: false);
                Assert.Equal(ClientDto.G9EUploadStatus.Completed, result.Status);
                received[protocol] = server.Wire.Received;
            }
            finally
            {
                File.Delete(source);
            }
        }

        output.WriteLine($"{Size:N0}-byte upload: server received {received[TestProtocol.Json]:N0} bytes over JSON, {received[TestProtocol.MessagePack]:N0} over MessagePack");
        Assert.True(received[TestProtocol.Json] > Size * 1.3, "JSON should carry the chunks as base64.");
        Assert.True(received[TestProtocol.MessagePack] < Size * 1.02, "MessagePack should carry the chunks as raw bytes.");
    }

    [Fact]
    public async Task A_server_that_only_speaks_Json_refuses_a_MessagePack_client_and_keeps_serving_Json()
    {
        await using var server = await TestServer.StartAsync(offerMessagePack: false);

        await using (var refused = new TestClient(server.BaseUrl, TestProtocol.MessagePack))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => refused.ConnectAsync());
            output.WriteLine(error.GetType().Name + ": " + error.Message);
            Assert.Contains("messagepack", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        await using var json = await server.ConnectAsync(TestProtocol.Json);
        Assert.Equal("Text", await json.Server.TransferFormat());
    }

    [Fact]
    public void The_app_shapes_are_asked_first_and_the_library_fills_the_gaps()
    {
        var app = TestShapes.GeneratedTypeShapeProvider;

        var client = G9SignalRSuperNetCoreClientMessagePack.CombineShapes(app);
        Assert.Same(app.GetTypeShape(typeof(string)), client.GetTypeShape(typeof(string)));
        Assert.NotNull(client.GetTypeShape(typeof(TestReading)));
        Assert.Same(G9SignalRSuperNetCoreClientMessagePack.LibraryShapes.GetTypeShape(typeof(ClientDto.G9DtUploadResult)),
            client.GetTypeShape(typeof(ClientDto.G9DtUploadResult)));
        Assert.Null(client.GetTypeShape(typeof(Uri)));
        Assert.Same(G9SignalRSuperNetCoreClientMessagePack.LibraryShapes, G9SignalRSuperNetCoreClientMessagePack.CombineShapes(null));

        var server = G9SignalRSuperNetCoreServerMessagePack.CombineShapes(app);
        Assert.Same(app.GetTypeShape(typeof(string)), server.GetTypeShape(typeof(string)));
        Assert.NotNull(server.GetTypeShape(typeof(ServerDto.G9DtUploadResult)));
        Assert.Same(G9SignalRSuperNetCoreServerMessagePack.LibraryShapes, G9SignalRSuperNetCoreServerMessagePack.CombineShapes(null));
    }

    [Fact]
    public void Every_type_the_upload_service_exchanges_has_a_library_shape_on_both_sides()
    {
        // Server: whatever IG9UploadService returns or streams is what a hub method built on it sends.
        var serverTypes = typeof(IG9UploadService).GetMethods()
            .SelectMany(m => new[] { m.ReturnType }.Concat(m.GetParameters().Select(p => p.ParameterType)))
            .Select(Unwrap)
            .Where(t => t is not null && t != typeof(CancellationToken) && t != typeof(void) && !typeof(Delegate).IsAssignableFrom(t))
            .Distinct()
            .ToList();
        Assert.Contains(typeof(ServerDto.G9DtBeginUploadResult), serverTypes);
        foreach (var type in serverTypes.Append(typeof(ServerDto.G9DtUploadProgress)).Append(typeof(Server.Classes.Presence.G9DtPresenceEvent)))
            Assert.True(G9SignalRSuperNetCoreServerMessagePack.LibraryShapes.GetTypeShape(type!) is not null, $"No server shape for {type}.");

        // Client: what G9CFileUploader / G9CFileDownloader invoke, stream and listen for.
        foreach (var type in new[]
                 {
                     typeof(ClientDto.G9DtBeginUploadResult), typeof(ClientDto.G9DtUploadResult), typeof(ClientDto.G9DtBeginDownloadResult),
                     typeof(ClientDto.G9DtUploadProgress), typeof(byte[]), typeof(string), typeof(long), typeof(int), typeof(bool)
                 })
            Assert.True(G9SignalRSuperNetCoreClientMessagePack.LibraryShapes.GetTypeShape(type) is not null, $"No client shape for {type}.");

        static Type? Unwrap(Type type)
        {
            if (!type.IsGenericType) return type;
            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(ValueTask<>) || definition == typeof(Task<>) || definition == typeof(IAsyncEnumerable<>)
                ? type.GetGenericArguments()[0]
                : type;
        }
    }

    private static async IAsyncEnumerable<int> Count(int from, int to)
    {
        for (var i = from; i <= to; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    private static string WriteRandomFile(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), "g9signalr-tests", $"source-{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(length));
        return path;
    }
}
