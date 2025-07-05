using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record PublisherRecord
{
    [JsonIgnore]
    public int UserId { get; set; }

    public Guid PublisherRef { get; set; }

    public required string Name { get; set; }
}

public record PublisherReadRecord : PublisherRecord
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    public DateTime LastModified { get; init; }
}
