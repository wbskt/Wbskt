using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record ClientRecord
{
    [JsonIgnore]
    public int UserId { get; init; }

    [JsonIgnore]
    public int ServerId { get; init; }

    public required string Name { get; init; }

    public Guid UniqueRef { get; init; }
}

public record ClientReadRecord : ClientRecord
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    public DateTime LastModified { get; init; }
}

public record ClientChannelRecord
{
    [JsonIgnore]
    public int ClientId { get; init; }

    [JsonIgnore]
    public int ChannelId { get; init; }
}

public record ClientChannelReadRecord : ClientChannelRecord
{
    [JsonIgnore]
    public DateTime LastModified { get; init; }

    [JsonIgnore]
    public bool Deleted { get; init; }
}

public record ClientChannels
{
    public Guid ClientRef { get; set; }
    public Guid[] ChannelRefs { get; set; } = [];
}

public record ChannelClients
{
    public Guid ChannelRef { get; set; }
    public Guid[] ClientRefs { get; set; } = [];
}
