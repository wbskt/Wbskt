using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record PublisherChannelRecord
{
    [JsonIgnore]
    public int PublisherId { get; init; }

    [JsonIgnore]
    public int ChannelId { get; init; }
}

public record PublisherChannelReadRecord : PublisherChannelRecord
{
    [JsonIgnore]
    public DateTime LastModified { get; init; }

    [JsonIgnore]
    public bool Deleted { get; init; }
}
