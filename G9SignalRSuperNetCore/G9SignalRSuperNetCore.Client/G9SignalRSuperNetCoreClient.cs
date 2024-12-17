using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using System.Linq.Expressions;

namespace G9SignalRSuperNetCore.Client
{
    public class G9SignalRSuperNetCoreClient<T> where T : class
    {
        protected readonly HubConnection Connection;

        public G9SignalRSuperNetCoreClient(string serverUrl)
        {
            Connection = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/hubServer", options =>
                {
                    //options.Transports = HttpTransportType.WebSockets;
                })
                .Build();
            Connection.ServerTimeout = TimeSpan.FromSeconds(60); // Wait for server response for 60 seconds
        }

        public async Task ConnectAsync()
        {
            Connection.On<string, string>("ReceiveMessage", (user, message) =>
            {
                Console.WriteLine($"{user}: {message}");
            });

            Connection.On<string, string>("FileUploaded", (fileName, filePath) =>
            {
                Console.WriteLine($"File {fileName} has been uploaded to {filePath}");
            });

            await Connection.StartAsync();
            Console.WriteLine("Connected to the server.");
        }

        public async Task SendAsync(Expression<Func<T, Task>> expression)
        {
            if (expression.Body is MethodCallExpression methodCall)
            {
                var methodName = methodCall.Method.Name;
                var arguments = methodCall.Arguments
                    .Select(arg => Expression.Lambda(arg).Compile().DynamicInvoke())
                    .ToArray();

                await Connection.InvokeAsync(methodName, arguments);
            }
            else
            {
                throw new ArgumentException("Invalid expression, expected a method call.", nameof(expression));
            }
        }

        public async Task SendAsync(Expression<Action<T>> expression)
        {
            if (expression.Body is MethodCallExpression methodCall)
            {
                var methodName = methodCall.Method.Name;
                var arguments = methodCall.Arguments
                    .Select(arg => Expression.Lambda(arg).Compile().DynamicInvoke())
                    .ToArray();

                await Connection.InvokeAsync(methodName, arguments);
            }
            else
            {
                throw new ArgumentException("Invalid expression, expected a method call.", nameof(expression));
            }
        }

        //public async Task UploadFileAsync(string filePath)
        //{
        //    var fileName = Path.GetFileName(filePath);
        //    var fileData = await File.ReadAllBytesAsync(filePath);

        //    await _connection.InvokeAsync("UploadFile", fileName, fileData);
        //}

        public async Task DisconnectAsync()
        {
            await Connection.StopAsync();
            Console.WriteLine("Disconnected from the server.");
        }
    }
}
