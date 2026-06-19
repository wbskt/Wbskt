using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record FailRunNode : BaseNode
{
    [JsonPropertyName("config")]
    public FailRunConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlFailRun;
}
