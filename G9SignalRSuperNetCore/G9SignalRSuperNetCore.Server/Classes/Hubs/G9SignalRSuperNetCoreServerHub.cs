using Microsoft.AspNetCore.SignalR;

namespace G9SignalRSuperNetCore.Server.Classes.Hubs
{
    public class G9SignalRSuperNetCoreServerHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }

        public async Task UploadFile(string fileName, byte[] fileData)
        {
            // Save the file to a location on the server
            Directory.CreateDirectory("UploadedFiles");
            var filePath = Path.Combine("UploadedFiles", fileName);
            await File.WriteAllBytesAsync(filePath, fileData);

            // Notify all clients about the uploaded file
            await Clients.All.SendAsync("FileUploaded", fileName, filePath);
        }
    }
}
