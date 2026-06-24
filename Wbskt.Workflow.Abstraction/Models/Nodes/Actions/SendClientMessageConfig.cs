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

}

