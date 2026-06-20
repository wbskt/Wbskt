using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record WebhookNotificationConfig
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("method")]
    public required string Method { get; init; }

    [JsonPropertyName("body")]
    public System.Text.Json.JsonElement? Body { get; init; } = null;

}

