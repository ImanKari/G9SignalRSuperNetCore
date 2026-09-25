namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Who shares one token bucket of a <see cref="G9AttrRateLimitAttribute"/> (2.9). The bucket is always per hub
///     method name; the scope decides what else it is keyed by.
/// </summary>
public enum G9ERateLimitScope
{
    /// <summary>
    ///     One bucket per connection (the behaviour of every version before 2.9, and still the default). A client that
    ///     opens a second connection gets a second allowance.
    /// </summary>
    Connection = 0,

    /// <summary>
    ///     One bucket per authenticated user (<c>HubCallerContext.UserIdentifier</c>), shared by every connection of that
    ///     user, so opening more connections does not buy more calls. A connection without a user identifier falls back
    ///     to its own per-connection bucket. The bucket is freed when the user's last connection disconnects.
    /// </summary>
    User = 1,

    /// <summary>
    ///     One bucket per remote IP address, shared by every connection from that address. A connection whose remote
    ///     address is unknown falls back to its own per-connection bucket. The bucket is freed when the last connection
    ///     from that address disconnects. Behind a reverse proxy, configure the forwarded-headers middleware, or every
    ///     client shares the proxy's address.
    /// </summary>
    Ip = 2
}
