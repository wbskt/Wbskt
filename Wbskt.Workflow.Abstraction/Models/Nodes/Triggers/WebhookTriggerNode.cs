using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed record WebhookTriggerNode : BaseNode
{
    [JsonPropertyName("config")]
    public required WebhookTriggerConfig Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.TriggerWebhook;
}
