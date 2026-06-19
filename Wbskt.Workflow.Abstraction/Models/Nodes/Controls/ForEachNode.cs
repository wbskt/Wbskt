using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ForEachNode : BaseNode
{
    [JsonPropertyName("config")]
    public ForEachConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlForEach;
}
