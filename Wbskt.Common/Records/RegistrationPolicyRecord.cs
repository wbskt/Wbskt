namespace Wbskt.Common.Records;

/// <summary>
/// Represents a registration policy, aligned with the dbo.RegistrationPolicies table.
/// </summary>
public record RegistrationPolicyRecord
{
    /// <summary>
    /// Internal database ID.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Publicly-facing unique identifier for the policy.
    /// </summary>
    public Guid RefId { get; init; }

    /// <summary>
    /// Human-readable name for the policy.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The ID of the user who owns this policy.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    /// The maximum number of clients that can be registered using this policy.
    /// Null for unlimited.
    /// </summary>
    public int? MaxClients { get; init; }

    /// <summary>
    /// The date and time when this policy expires.
    /// Null for no expiry.
    /// </summary>
    public DateTime? Expiry { get; init; }

    /// <summary>
    /// A PIN code associated with the policy for certain registration types.
    /// </summary>
    public int Pin { get; init; }

    /// <summary>
    /// Timestamp of the last modification.
    /// </summary>
    public DateTime LastModified { get; init; }
}
