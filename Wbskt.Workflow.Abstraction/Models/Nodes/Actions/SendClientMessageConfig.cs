using Wbskt.Models;
using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Templates;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendClientMessageConfig
{
    [JsonPropertyName("clientRef")]
    public string ClientRef { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [Templated]
    [JsonPropertyName("payload")]
    public System.Text.Json.JsonElement? Payload { get; init; } = null;

    /// <summary>
    /// Wait for the client to acknowledge the command before moving on, leaving by "acked", "failed"
    /// or "timeout" instead of "default". Off by default, so existing workflows keep moving at once.
    /// </summary>
    [JsonPropertyName("awaitAck")]
    public bool AwaitAck { get; init; } = false;

    /// <summary>How long to wait for the acknowledgement; <see cref="DefaultAckTimeout"/> when unset.</summary>
    [JsonPropertyName("ackTimeout")]
    public TimeSpan? AckTimeout { get; init; } = null;

    public static readonly TimeSpan DefaultAckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The step's command expires when it stops waiting, so the wait is bounded by the command's lifetime.</summary>
    public static readonly TimeSpan MaxAckTimeout = CommandLifetime.Max;

}

