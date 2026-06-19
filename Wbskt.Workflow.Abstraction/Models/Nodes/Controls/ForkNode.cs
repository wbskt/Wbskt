using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record ForkNode : BaseNode
{
    [JsonPropertyName("config")]
    public ForkConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlFork;
}
