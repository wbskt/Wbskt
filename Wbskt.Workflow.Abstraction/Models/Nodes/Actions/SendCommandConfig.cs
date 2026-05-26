using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record SendCommandConfig(
    [property: JsonPropertyName("deviceRef")] string DeviceRef,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement? Payload = null);
