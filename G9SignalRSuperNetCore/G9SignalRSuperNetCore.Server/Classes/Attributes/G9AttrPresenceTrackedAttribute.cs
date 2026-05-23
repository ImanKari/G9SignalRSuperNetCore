namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Marks a hub type as presence-tracked. The hub filter (<c>G9CHubFilter</c>) routes
///     connect/disconnect lifecycle calls through <c>G9CPresenceTracker</c> so the tracker
///     publishes <c>G9DtPresenceEvent</c>s as users come online and go offline.
/// </summary>
/// <remarks>
///     Zero-cost when not used: the tracker is only resolved if the consumer calls
///     <c>AddG9SignalRSuperNetCorePresence</c> at startup. Hubs without this attribute pay
///     no extra work on connect/disconnect.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class G9AttrPresenceTrackedAttribute : Attribute
{
}
