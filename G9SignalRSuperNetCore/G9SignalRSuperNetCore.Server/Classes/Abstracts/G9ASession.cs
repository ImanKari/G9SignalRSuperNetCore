using System.Net;

namespace G9SignalRSuperNetCore.Server.Classes.Abstracts;

public abstract class G9ASession
{
    internal int ConnectionCounts { set; get; }

    public IPAddress? ClientIpAddress { internal set; get; }

    protected virtual Task OnDisconnected(Exception? exception)
    {

        return Task.CompletedTask;
    }

    protected virtual Task OnConnectedAsync()
    {

        return Task.CompletedTask;
    }

    protected virtual Task OnDisposeAsync()
    {

        return Task.CompletedTask;
    }
}