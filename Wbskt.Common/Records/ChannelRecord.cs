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
    public Guid SubscriptionRef { get; set; }
}

public record ChannelReadRecord : ChannelRecord
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    public DateTime LastModified { get; init; }
}
