using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ParallelForEachNode(string NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] ParallelForEachConfig? Config) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ControlParallelForEach;
}
