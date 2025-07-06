using System.Text.Json.Serialization;

namespace Wbskt.Common.Contracts;

public class ClientPayload
{

    [JsonIgnore]
    public int ChannelId { get; set; }

    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// The publisher id to which the payload needs to be sent.
    /// </summary>
    public Guid PublisherRef { get; set; }

    public bool EnsureDelivery { get; set; }

    /// <summary>
    /// [Internal but available to clients]
    /// </summary>
    public Guid ChannelRef { get; set; }

    public Guid PayloadId { get; set; }
}
