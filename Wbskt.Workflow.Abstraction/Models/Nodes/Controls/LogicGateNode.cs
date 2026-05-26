using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record LogicGateNode(Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] LogicGateConfig? Config) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ControlLogic;
}
