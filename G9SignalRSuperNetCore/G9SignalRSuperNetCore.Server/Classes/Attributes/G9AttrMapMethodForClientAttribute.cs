namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Specifies that a method is mapped for invocation by the client in a SignalR hub.
/// </summary>
/// <remarks>
///     This attribute is used to mark server-side methods that are exposed for client-side invocation.
///     It can only be applied to methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class G9AttrMapMethodForClientAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="G9AttrMapMethodForClientAttribute" /> class.
    /// </summary>
    public G9AttrMapMethodForClientAttribute()
    {
    }
}