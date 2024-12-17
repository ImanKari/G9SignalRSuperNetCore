namespace G9SignalRSuperNetCore.WebServer;

public interface CustomClientInterfaceWithSession
{
    public Task LoginResult(bool accepted);
}