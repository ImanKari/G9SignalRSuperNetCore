using System.Diagnostics;

namespace G9SignalRSuperNetCore.ConsoleClient;

public interface MyInterface
{
    public void MethodA(int a);

    public void MethodB(int a, int b);
}

internal class Program
{
    private static async Task Main(string[] args)
    {
        //var client = new G9SignalRSuperNetCoreClient<MyInterface>("https://localhost:7159");

        //await client.ConnectAsync();

        //var client = new G9CCustomHubClient("https://localhost:7159");
        //await client.ConnectAsync();
        //await client.Server.Login("Iman", "@ImanKari1990");


        //while (true)
        //{
        //    Console.WriteLine("Enter command:");
        //    var message = Console.ReadLine();
        //    if (message?.ToLower() == "exit")
        //        break;
        //    if (message?.ToLower() == "file")
        //    {
        //        //await client.UploadFileAsync("Raspberry SIM7600 4G.mp4");
        //    }
        //    if (message?.ToLower() == "replay")
        //    {
        //        await client.Server.Replay(0.ToString());
        //    }

        //}


        var token = string.Empty;

        var client = new CustomHubWithJWTAuthAndSessionClientWithJWTAuth("https://localhost:7159");


        var hasResult = false;
        var isAccepted = false;
        string authReason = null;
        await client.Authorize(
            "jg93w4t9swhuwgvosedrgf029ptg2qw38r0dfgw239p84521039r8hwaqfy8o923519723rgfw923w4ty#$&Y#$WUYHW#$&YW@#$TG@#$^#$",
            result =>
            {
                isAccepted = result.IsAccepted;
                authReason = result.RejectionReason;
                hasResult = true;
                token = result.JWToken;
                return Task.CompletedTask;
            });


        while (!hasResult && string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Wait for auth result.");
            Task.Delay(369);
            if (hasResult)
            {
                if (isAccepted)
                {
                    Console.WriteLine("Auth is accepted.");
                    break;
                }

                Console.WriteLine($"Fail auth, reason: {authReason}");
                _ = Console.ReadLine();
                return;
            }
        }

        await client.ConnectAsync(token);
        await client.Server.Login("Iman", "@ImanKari1990");
        try
        {
            await client.Connection.SendCoreAsync("ValidateUserAndGenerateJWToken", new[] { "Hello" });
        }
        catch (Exception es)
        {
            Debug.WriteLine(es);
        }

        client.AssignListenerEvent(
            s => s.Replay, (string param) =>
            {
                Console.WriteLine($"AssignListenerEvent: {param}");
                return Task.CompletedTask;
            });

        while (true)
        {
            Console.WriteLine("Enter command or message:");
            var message = Console.ReadLine();
            if (message?.ToLower() == "exit")
                break;
            if (message?.ToLower() == "file")
            {
                //await client.UploadFileAsync("Raspberry SIM7600 4G.mp4");
            }


            if (message?.ToLower() == "result")
                try
                {
                    var result = await client.SendThenListenOnceAsync<string, string>(
                        sendPart => sendPart.TestResult("Test1", "Tset2"),
                        s => s.TestResult);

                    //var result2 = await client.ListenOnceAsync<string>(s => s.Replay);
                    Console.WriteLine(
                        $"Client Receive Result '{result.Item1}|{result.Item2}' by method: {nameof(client.ListenOnceAsync)}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

            await client.Server.Replay(message);
        }

        await client.DisconnectAsync();
    }
}