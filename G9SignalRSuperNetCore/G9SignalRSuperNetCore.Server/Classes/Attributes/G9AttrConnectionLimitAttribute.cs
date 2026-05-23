namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Caps the number of simultaneous connections that may be open for a given user or
///     remote IP. Applied at the hub class level so the limit is checked on every connect.
/// </summary>
/// <remarks>
///     <para>When a new connection would exceed either limit it is aborted before
///     <see cref="Microsoft.AspNetCore.SignalR.Hub.OnConnectedAsync"/> returns, and a
///     <see cref="Microsoft.AspNetCore.SignalR.HubException"/> with
///     <see cref="Errors.G9CErrorCodes.ConnectionLimit"/> is sent to the caller.</para>
///     <para>Tracking is in-process and lock-free using
///     <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>.
///     For cluster-wide enforcement use a distributed limiter (Bundle 5).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrConnectionLimitAttribute : Attribute
{
    /// <summary>Maximum simultaneous connections per authenticated user. Zero or negative disables the user check.</summary>
    public int PerUser { get; }

    /// <summary>Maximum simultaneous connections per remote IP. Zero or negative disables the IP check.</summary>
    public int PerIp { get; }

    /// <summary>Initializes a new connection-limit attribute.</summary>
    /// <param name="perUser">Maximum simultaneous connections per authenticated user.</param>
    /// <param name="perIp">Maximum simultaneous connections per remote IP.</param>
    public G9AttrConnectionLimitAttribute(int perUser = 0, int perIp = 0)
    {
        PerUser = perUser;
        PerIp = perIp;
    }
}
