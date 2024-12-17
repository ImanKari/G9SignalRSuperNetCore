namespace G9SignalRSuperNetCore.WebServer;

public interface CustomClientInterface
{
    /// <summary>
    /// Method to receive the result for login
    /// </summary>
    /// <param name="accepted">Specifies that access is true or not</param>
    public Task LoginResult(bool accepted);

    public Task Replay(string message);

}