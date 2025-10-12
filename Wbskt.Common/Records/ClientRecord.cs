using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
/// Represents a client record, aligned with the dbo.Clients table.
/// </summary>
public record ClientRecord
{
    /// <summary>
    /// Internal database ID.
    /// </summary>
    [JsonIgnore]
    public int Id { get; init; }

    /// <summary>
    /// Publicly-facing unique identifier for the client.
    /// </summary>
    public Guid RefId { get; init; }

    /// <summary>
    /// The ID of the user who owns this client.
    /// </summary>
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    /// The ID of the registration policy this client is bound to.
    /// </summary>
    [JsonIgnore]
    public int RegistrationPolicyId { get; set; }

    /// <summary>
    /// Optional, human-readable name for the client.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Indicates if the client is active.
    /// </summary>
    public bool Active { get; set; }
}

// Note: The records below are for client-channel relationships and may need to be reviewed
// in a subsequent step to ensure they align with the overall new design.

/// <summary>
///     Client-channel relationship for bulk operations.
/// </summary>
public record ClientChannelRecord
{
    /// <summary>
    ///     Internal client ID.
    /// </summary>
    [JsonIgnore]
    public int ClientId { get; init; }

    /// <summary>
    ///     Internal channel ID.
    /// </summary>
    [JsonIgnore]
    public int ChannelId { get; init; }
}

/// <summary>
///     Client-channel relationship with cache metadata.
/// </summary>
public record ClientChannelReadRecord : ClientChannelRecord
{
    /// <summary>
    ///     Timestamp when the relationship was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }

    /// <summary>
    ///     Indicates if the relationship has been soft deleted.
    /// </summary>
    [JsonIgnore]
    public bool Deleted { get; init; }
}

/// <summary>
///     DTO for client-channel relationships using public GUIDs.
/// </summary>
public record ClientChannels
{
    /// <summary>
    ///     Public client reference GUID.
    /// </summary>
    public Guid ClientRef { get; set; }

    /// <summary>
    ///     Array of channel reference GUIDs that the client is subscribed to.
    /// </summary>
    public Guid[] ChannelRefs { get; set; } = [];
}

/// <summary>
///     DTO for channel-client relationships using public GUIDs.
/// </summary>
public record ChannelClients
{
    /// <summary>
    ///     Public channel reference GUID.
    /// </summary>
    public Guid ChannelRef { get; set; }

    /// <summary>
    ///     Array of client reference GUIDs subscribed to this channel.
    /// </summary>
    public Guid[] ClientRefs { get; set; } = [];
}
