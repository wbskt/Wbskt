using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record ParallelForEachNode : BaseNode
{
    [JsonPropertyName("config")]
    public ParallelForEachConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlParallelForEach;
}
