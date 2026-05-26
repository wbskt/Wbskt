using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Triggers;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;

public sealed record ManualTriggerNode(
    string NodeId,
    string Name,
    IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] ManualTriggerConfig Config
) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.TriggerManual;
}
