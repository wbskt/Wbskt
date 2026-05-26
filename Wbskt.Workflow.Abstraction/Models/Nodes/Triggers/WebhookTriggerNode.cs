using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed record WebhookTriggerNode(
    Guid NodeId,
    string Name,
    IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] WebhookTriggerConfig Config
) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.TriggerWebhook;
}
