using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record LogicGateNode : BaseNode
{
    [JsonPropertyName("config")]
    public LogicGateConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlLogic;
}
