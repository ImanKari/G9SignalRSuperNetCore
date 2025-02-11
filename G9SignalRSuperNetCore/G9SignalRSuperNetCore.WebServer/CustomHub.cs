﻿using G9SignalRSuperNetCore.Server.Classes.Abstracts;
using G9SignalRSuperNetCore.Server.Classes.Attributes;

namespace G9SignalRSuperNetCore.WebServer;

public class CustomHub : G9AHubBase<CustomHub, CustomClientInterface>
{
    public override string RoutePattern()
    {
        return "/ApplicationHub";
    }

    /// <summary>
    /// Information
    /// </summary>
    /// <param name="userName">user Name</param>
    /// <param name="password">Password</param>
    public async Task Login(string userName, string password)
    {
        await Clients.Caller.LoginResult(true);
        
    }

    /// <summary>
    /// Replay
    /// </summary>
    /// <param name="message">Okay</param>
    public async Task Replay(string message)
    {
        Console.WriteLine(Context.ConnectionId);
        await Clients.Caller.Replay(message);
    }


    private async Task Method1(string message)
    {
        await Task.CompletedTask;
    }

    protected async Task Method2(string message)
    {
        await Task.CompletedTask;
    }
}