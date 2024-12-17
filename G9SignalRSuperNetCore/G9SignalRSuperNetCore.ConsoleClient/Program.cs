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
            var client = new G9SignalRSuperNetCoreClient<MyInterface>("https://localhost:7159");

            await client.ConnectAsync();

            Console.WriteLine("Enter your username:");
            var user = Console.ReadLine();

            while (true)
            {
                var message = Console.ReadLine();
                if (message?.ToLower() == "exit")
                    break;
                if (message?.ToLower() == "file")
                {
                    //await client.UploadFileAsync("Raspberry SIM7600 4G.mp4");
                }

                await client.SendAsync(s => s.MethodA(1));
            }

            await client.DisconnectAsync();
        }
    }
}
