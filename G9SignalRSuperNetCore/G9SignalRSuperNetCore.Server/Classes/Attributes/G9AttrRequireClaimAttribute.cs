namespace G9SignalRSuperNetCore.Server.Classes.Attributes;

/// <summary>
///     Requires that the authenticated principal carry a claim with the specified type and
///     (optionally) one of the specified values.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class G9AttrRequireClaimAttribute : Attribute
{
    /// <summary>The required claim type, e.g. <c>"scope"</c>.</summary>
    public string ClaimType { get; }

    /// <summary>
    ///     Accepted claim values. When empty, any claim with the given <see cref="ClaimType"/>
    ///     is accepted.
    /// </summary>
    public string[] AcceptedValues { get; }

    /// <summary>Initializes a new claim requirement.</summary>
    /// <param name="claimType">The claim type that must be present.</param>
    /// <param name="acceptedValues">Optional list of acceptable values; when empty any value is accepted.</param>
    public G9AttrRequireClaimAttribute(string claimType, params string[] acceptedValues)
    {
        if (string.IsNullOrEmpty(claimType)) throw new ArgumentException("Claim type required.", nameof(claimType));
        ClaimType = claimType;
        AcceptedValues = acceptedValues ?? Array.Empty<string>();
    }
}
