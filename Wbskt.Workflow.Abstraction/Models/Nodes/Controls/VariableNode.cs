using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record VariableNode : BaseNode
{
    [JsonPropertyName("config")]
    public VariableConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlVariable;
}
