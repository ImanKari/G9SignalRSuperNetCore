namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Rejects invocations that arrive before the SignalR connection is fully established.
///     Useful as belt-and-braces for hub methods that assume <c>OnConnectedAsync</c> has run.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrConnectionRequiredAttribute : Attribute
{
}
