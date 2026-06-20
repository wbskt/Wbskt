using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendCommandConfig
{
    [JsonPropertyName("deviceRef")]
    public required string DeviceRef { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("payload")]
    public System.Text.Json.JsonElement? Payload { get; init; } = null;

}

