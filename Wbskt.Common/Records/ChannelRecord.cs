using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record ChannelRecord
{
    [JsonIgnore]
    public int UserId { get; set; }

    /// <summary>
    /// Human-readable name for the channel
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Used for a client to connect to a channel
    /// </summary>
    public Guid ChannelRef { get; set; }
}

public record ChannelReadRecord : ChannelRecord
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

public record ChannelPublishers
{
    public Guid ChannelRef { get; set; }
    public Guid[] PublisherRefs { get; set; } = [];
}
