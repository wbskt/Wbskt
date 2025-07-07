using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
///     Channel entity for creating and updating channel data.
/// </summary>
public record ChannelRecord
{
    /// <summary>
    ///     Internal user ID who owns this channel.
    /// </summary>
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    ///     Human-readable name for the channel.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Unique identifier for the channel used in public APIs.
    /// </summary>
    public Guid ChannelRef { get; set; }
}

/// <summary>
///     Channel entity with metadata for reading operations.
/// </summary>
public record ChannelReadRecord : ChannelRecord
{
    /// <summary>
    ///     Internal database ID.
    /// </summary>
    [JsonIgnore]
    public int Id { get; init; }

    /// <summary>
    ///     Timestamp when the channel was last modified.
    /// </summary>
    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

/// <summary>
///     DTO for channel-publisher relationships using public GUIDs.
/// </summary>
public record ChannelPublishers
{
    /// <summary>
    ///     Public channel reference GUID.
    /// </summary>
    public Guid ChannelRef { get; set; }

    /// <summary>
    ///     Array of publisher reference GUIDs associated with this channel.
    /// </summary>
    public Guid[] PublisherRefs { get; set; } = [];
}
