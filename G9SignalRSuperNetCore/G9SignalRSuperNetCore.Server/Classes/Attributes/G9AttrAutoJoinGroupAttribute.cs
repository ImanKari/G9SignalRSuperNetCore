namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Marks a hub type so that every newly-connected client is automatically added to the
///     declared group(s). Useful for "everyone joins the lobby" patterns. The hub filter
///     (<c>G9CGroupAutoJoinFilter</c>) reads this attribute on connect and dispatches the
///     <see cref="Groups.G9CGroupManager{THub}.JoinAsync"/> call.
/// </summary>
/// <remarks>
///     <para>Zero-cost when not used: the filter is registered through
///     <c>AddG9SignalRSuperNetCoreGroups</c>; if the consumer never calls that DI extension,
///     the filter is never resolved and no per-connection work is performed.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class G9AttrAutoJoinGroupAttribute : Attribute
{
    /// <summary>The group name to join.</summary>
    public string GroupName { get; }

    /// <summary>Initializes the attribute with a fixed <paramref name="groupName"/>.</summary>
    public G9AttrAutoJoinGroupAttribute(string groupName)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupName);
        GroupName = groupName;
    }
}
