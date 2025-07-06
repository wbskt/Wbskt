using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record PublisherRecord
{
    [JsonIgnore]
    public int UserId { get; set; }

    public Guid PublisherRef { get; init; }

    public required string Name { get; init; }
}

public record PublisherReadRecord : PublisherRecord
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

public record PublisherChannels
{
    public Guid PublisherRef { get; set; }
    public Guid[] ChannelRefs { get; set; } = [];
}
