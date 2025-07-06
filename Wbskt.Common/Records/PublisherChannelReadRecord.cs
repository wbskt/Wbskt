using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

/// <summary>
/// Publisher-channel relationship for bulk operations.
/// </summary>
public record PublisherChannelRecord
{
    /// <summary>
    /// Internal publisher ID.
    /// </summary>
    [JsonIgnore]
    public int PublisherId { get; init; }

    /// <summary>
    /// Internal channel ID.
    /// </summary>
    [JsonIgnore]
    public int ChannelId { get; init; }
}

/// <summary>
/// Publisher-channel relationship with cache metadata.
/// </summary>
public record PublisherChannelReadRecord : PublisherChannelRecord
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
