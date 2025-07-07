using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
///     Enrollment policy types.
/// </summary>
public enum EnrollmentPolicyType
{
    /// <summary>
    ///     Time-limited policy with expiry date.
    /// </summary>
    TimeLimited = 1,

    /// <summary>
    ///     Policy limited by number of clients.
    /// </summary>
    NumberOfClients = 2,

    /// <summary>
    ///     Unlimited policy with no restrictions.
    /// </summary>
    Unlimited = 3,

    /// <summary>
    ///     Policy limited by both time and number of clients.
    /// </summary>
    TimeAndCount = 4
}

/// <summary>
///     Enrollment policy entity for creating new policies.
/// </summary>
public record EnrollmentPolicyRecord
{
    /// <summary>
    ///     Internal user ID who owns this policy.
    /// </summary>
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    ///     Unique policy reference GUID.
    /// </summary>
    public Guid PolicyRef { get; set; }

    /// <summary>
    ///     Human-readable name for the policy.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Type of enrollment policy.
    /// </summary>
    public required EnrollmentPolicyType PolicyType { get; init; }

    /// <summary>
    ///     Maximum number of clients for NumberOfClients and TimeAndCount policies.
    /// </summary>
    public int? MaxClients { get; init; }

    /// <summary>
    ///     Expiry date for TimeLimited and TimeAndCount policies.
    /// </summary>
    public DateTime? ExpiryDate { get; init; }
}

/// <summary>
///     Enrollment policy entity with metadata for reading operations.
/// </summary>
public record EnrollmentPolicyReadRecord : EnrollmentPolicyRecord
{
    /// <summary>
    ///     Internal database ID.
    /// </summary>
    [JsonIgnore]
    public int Id { get; init; }

    /// <summary>
    ///     Current number of clients registered using this policy.
    /// </summary>
    public int CurrentUsage { get; init; }

    /// <summary>
    ///     Whether the policy is currently active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    ///     Timestamp when the policy was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }
}
