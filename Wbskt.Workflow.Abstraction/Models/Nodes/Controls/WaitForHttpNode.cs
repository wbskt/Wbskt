using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record WaitForHttpNode : BaseNode
{
    [JsonPropertyName("config")]
    public WaitForHttpConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlWaitForHttp;
}
