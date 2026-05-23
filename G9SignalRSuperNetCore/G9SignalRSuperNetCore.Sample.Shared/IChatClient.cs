using G9SignalRSuperNetCore.Server.Classes.FileUpload;

namespace G9SignalRSuperNetCore.Sample.Shared;

/// <summary>
///     Server-to-client callbacks for the sample chat hub.
///     Both the server and the generated client compile against this single definition.
/// </summary>
public interface IChatClient
{
    /// <summary>Pushes a chat message to a connected client.</summary>
    /// <param name="user">The display name of the sender.</param>
    /// <param name="message">The message text.</param>
    Task ReceiveMessage(string user, string message);

    /// <summary>Notifies a client that a user joined the room.</summary>
    /// <param name="user">The user that joined.</param>
    Task UserJoined(string user);

    /// <summary>Notifies a client that a user left the room.</summary>
    /// <param name="user">The user that left.</param>
    Task UserLeft(string user);

    /// <summary>Acknowledges the result of a login attempt.</summary>
    /// <param name="accepted">True when the credentials were accepted.</param>
    Task LoginResult(bool accepted);

    /// <summary>Server-pushed file-upload progress.</summary>
    /// <param name="progress">The current bytes-received state.</param>
    Task UploadProgress(G9DtUploadProgress progress);
}
