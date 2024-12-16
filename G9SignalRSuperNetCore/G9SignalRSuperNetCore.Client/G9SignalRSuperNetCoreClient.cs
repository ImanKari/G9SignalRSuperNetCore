using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace G9SignalRSuperNetCore.Client
{
    public class G9SignalRSuperNetCoreClient
    {
        private readonly HubConnection _connection;

        public G9SignalRSuperNetCoreClient(string serverUrl)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/serverHub", options =>
                {
                    //options.Transports = HttpTransportType.WebSockets;
                })
                .Build();
            _connection.ServerTimeout = TimeSpan.FromSeconds(60); // Wait for server response for 60 seconds
        }

        public async Task ConnectAsync()
        {
            _connection.On<string, string>("ReceiveMessage", (user, message) =>
            {
                Console.WriteLine($"{user}: {message}");
            });

            _connection.On<string, string>("FileUploaded", (fileName, filePath) =>
            {
                Console.WriteLine($"File {fileName} has been uploaded to {filePath}");
            });

            await _connection.StartAsync();
            Console.WriteLine("Connected to the server.");
        }

        public async Task SendMessageAsync(string user, string message)
        {
            await _connection.InvokeAsync("SendMessage", user, message);
        }

        public async Task UploadFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var fileData = await File.ReadAllBytesAsync(filePath);

            await _connection.InvokeAsync("UploadFile", fileName, fileData);
        }

        public async Task DisconnectAsync()
        {
            await _connection.StopAsync();
            Console.WriteLine("Disconnected from the server.");
        }
    }
}
