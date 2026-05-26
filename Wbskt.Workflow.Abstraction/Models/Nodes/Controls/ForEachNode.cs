using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ForEachNode(Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] ForEachConfig? Config) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ControlForEach;
}
