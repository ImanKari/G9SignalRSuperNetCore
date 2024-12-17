using G9SignalRSuperNetCore.Client;

namespace G9SignalRSuperNetCore.ConsoleClient
{
    public interface MyInterface
    {
        public void MethodA(int a);

        public void MethodB(int a, int b);
    }

    internal class Program
    {
        static async Task Main(string[] args)
        {
            //var client = new G9SignalRSuperNetCoreClient<MyInterface>("https://localhost:7159");

            //await client.ConnectAsync();

            var client = new G9CCustomHubClient("https://localhost:7159");
            await client.ConnectAsync();
            await client.Server.Login("Iman", "@ImanKari1990");


            while (true)
            {
                Console.WriteLine("Enter command:");
                var message = Console.ReadLine();
                if (message?.ToLower() == "exit")
                    break;
                if (message?.ToLower() == "file")
                {
                    //await client.UploadFileAsync("Raspberry SIM7600 4G.mp4");
                }
                if (message?.ToLower() == "replay")
                {
                    await client.Server.Replay(0.ToString());
                }

            }

            await client.DisconnectAsync();
        }
    }
}
