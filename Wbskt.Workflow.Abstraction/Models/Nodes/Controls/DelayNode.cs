using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record DelayNode : BaseNode
{
    [JsonPropertyName("config")]
    public DelayConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlDelay;
}
