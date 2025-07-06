using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
/// Publisher entity for creating and updating publisher data.
/// </summary>
public record PublisherRecord
{
    /// <summary>
    /// Internal user ID who owns this publisher.
    /// </summary>
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    /// Unique identifier for the publisher used in public APIs.
    /// </summary>
    public Guid PublisherRef { get; init; }

    /// <summary>
    /// Human-readable name for the publisher.
    /// </summary>
    public required string Name { get; init; }
}

/// <summary>
/// Publisher entity with metadata for reading operations.
/// </summary>
public record PublisherReadRecord : PublisherRecord
{
    /// <summary>
    /// Internal database ID.
    /// </summary>
    [JsonIgnore]
    public int Id { get; init; }

    /// <summary>
    /// Timestamp when the publisher was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

/// <summary>
/// DTO for publisher-channel relationships using public GUIDs.
/// </summary>
public record PublisherChannels
{
    /// <summary>
    /// Public publisher reference GUID.
    /// </summary>
    public Guid PublisherRef { get; set; }

    /// <summary>
    /// Array of channel reference GUIDs that the publisher is associated with.
    /// </summary>
    public Guid[] ChannelRefs { get; set; } = [];
}
