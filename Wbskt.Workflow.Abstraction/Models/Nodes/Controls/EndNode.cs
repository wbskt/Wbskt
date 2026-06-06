using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

/// <summary>
/// Explicit terminal node — completes the branch when reached (no outbound edges required).
/// </summary>
public sealed record EndNode(Guid NodeId, string Name, IReadOnlyCollection<PortDefinition> Ports)
    : BaseNode(NodeId, Name, Ports)
{
    [JsonIgnore]
    public override string Kind => NodeKind.ControlEnd;
}
