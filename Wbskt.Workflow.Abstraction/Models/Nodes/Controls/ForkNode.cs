using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record ForkNode(Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports,
    [property: JsonPropertyName("config")] ForkConfig? Config) : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ControlFork;
}
