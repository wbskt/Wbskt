using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendClientMessageConfig
{
    [JsonPropertyName("clientRef")]
    public required string ClientRef { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("payload")]
    public System.Text.Json.JsonElement? Payload { get; init; } = null;

    /// <summary>
    /// Wait for the device to acknowledge the command before moving on, leaving by "acked", "failed"
    /// or "timeout" instead of "default". Off by default, so existing workflows keep moving at once.
    /// </summary>
    [JsonPropertyName("awaitAck")]
    public bool AwaitAck { get; init; } = false;

    /// <summary>How long to wait for the acknowledgement; <see cref="DefaultAckTimeout"/> when unset.</summary>
    [JsonPropertyName("ackTimeout")]
    public TimeSpan? AckTimeout { get; init; } = null;

    public static readonly TimeSpan DefaultAckTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MaxAckTimeout = TimeSpan.FromHours(24);

}

