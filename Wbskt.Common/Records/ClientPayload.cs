using System.Text.Json.Serialization;

namespace Wbskt.Common.Records;

public record ClientPayload
{

    [JsonIgnore]
    public int ChannelId { get; set; }

    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// The publisher id to which the payload needs to be sent.
    /// </summary>
    public Guid PublisherRef { get; init; }

    public bool EnsureDelivery { get; set; }

    /// <summary>
    /// [Internal but available to clients]
    /// </summary>
    public Guid ChannelRef { get; set; }

    public Guid PayloadId { get; set; }
}
