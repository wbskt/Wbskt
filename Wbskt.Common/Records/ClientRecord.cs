using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
/// Client entity for creating and updating client data.
/// </summary>
public record ClientRecord
{
    /// <summary>
    /// Internal user ID who owns this client.
    /// </summary>
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    /// Internal server ID where this client is assigned.
    /// </summary>
    [JsonIgnore]
    public int ServerId { get; init; }

    /// <summary>
    /// Internal enrollment policy ID for this client.
    /// </summary>
    [JsonIgnore]
    public int PolicyId { get; set; }

    /// <summary>
    /// Human-readable name for the client.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Unique identifier for the client used in public APIs.
    /// </summary>
    public Guid UniqueRef { get; init; }

    /// <summary>
    /// Enrollment policy reference.
    /// </summary>
    public Guid PolicyRef { get; set; }
}

/// <summary>
/// Client entity with metadata for reading operations.
/// </summary>
public record ClientReadRecord : ClientRecord
{
    /// <summary>
    /// Internal database ID.
    /// </summary>
    [JsonIgnore]
    public int Id { get; init; }

    /// <summary>
    /// Timestamp when the client was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Client-channel relationship for bulk operations.
/// </summary>
public record ClientChannelRecord
{
    /// <summary>
    /// Internal client ID.
    /// </summary>
    [JsonIgnore]
    public int ClientId { get; init; }

    /// <summary>
    /// Internal channel ID.
    /// </summary>
    [JsonIgnore]
    public int ChannelId { get; init; }
}

/// <summary>
/// Client-channel relationship with cache metadata.
/// </summary>
public record ClientChannelReadRecord : ClientChannelRecord
{
    /// <summary>
    /// Timestamp when the relationship was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }

    /// <summary>
    /// Indicates if the relationship has been soft deleted.
    /// </summary>
    [JsonIgnore]
    public bool Deleted { get; init; }
}

/// <summary>
/// DTO for client-channel relationships using public GUIDs.
/// </summary>
public record ClientChannels
{
    /// <summary>
    /// Public client reference GUID.
    /// </summary>
    public Guid ClientRef { get; set; }

    /// <summary>
    /// Array of channel reference GUIDs that the client is subscribed to.
    /// </summary>
    public Guid[] ChannelRefs { get; set; } = [];
}

/// <summary>
/// DTO for channel-client relationships using public GUIDs.
/// </summary>
public record ChannelClients
{
    /// <summary>
    /// Public channel reference GUID.
    /// </summary>
    public Guid ChannelRef { get; set; }

    /// <summary>
    /// Array of client reference GUIDs subscribed to this channel.
    /// </summary>
    public Guid[] ClientRefs { get; set; } = [];
}
