using System.Text.Json;

namespace G9SignalRSuperNetCore.ConsoleClient;

internal class Program
{
    private static async Task Main(string[] args)
    {
        // The typed client below (CustomHubWithJWTAuthAndSessionClientWithJWTAuth) is produced
        // by the build-time source generator from the WebServer project's hubs and copied
        // into this project as GeneratedClientTest.cs.
        var client = new CustomHubWithJWTAuthAndSessionClientWithJWTAuth("https://localhost:7159");

        var authResult = await client.AuthorizeAsync(
            "jg93w4t9swhuwgvosedrgf029ptg2qw38r0dfgw239p84521039r8hwaqfy8o923519723rgfw923w4ty#$&Y#$WUYHW#$&YW@#$TG@#$^#$");

        if (!authResult.IsAccepted)
        {
            Console.WriteLine($"Auth rejected: {authResult.RejectionReason}");
            return;
        }

        await client.ConnectAsync();
        Console.WriteLine("Connected.");

        await client.Server.Login("Iman", "@ImanKari1990");

        var receiveListDataTest = await client.Server.GetList();
        Console.WriteLine($"GetList returned {receiveListDataTest.Count} items");

        var receiveDataTypeTest = await client.Server.GetDataType();
        if (!receiveDataTypeTest.RequestStatus)
        {
            Console.WriteLine("GetDataType failed");
        }
        else
        {
            var dataJson = receiveDataTypeTest.Data?.ToString() ?? "[]";
            var itemList = JsonSerializer.Deserialize<List<string>>(dataJson) ?? new List<string>();
            Console.WriteLine($"GetDataType returned {itemList.Count} items");
        }

        while (true)
        {
            Console.WriteLine("Enter command or message:");
            var message = Console.ReadLine();
            if (string.Equals(message, "exit", StringComparison.OrdinalIgnoreCase)) break;
            await client.Server.Replay(message ?? string.Empty);
        }

        await client.DisconnectAsync();
    }
}
