using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record JoinNode : BaseNode
{
    [JsonPropertyName("config")]
    public JoinConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlJoin;
}
