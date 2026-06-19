using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record AwaitSignalNode : BaseNode
{
    [JsonPropertyName("config")]
    public AwaitSignalConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlAwaitSignal;
}
