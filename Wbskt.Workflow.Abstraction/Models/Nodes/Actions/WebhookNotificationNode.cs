using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
public sealed record WebhookNotificationNode : BaseActionNode
{
    [JsonPropertyName("config")]
    public required WebhookNotificationConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ActionWebhook;
}
