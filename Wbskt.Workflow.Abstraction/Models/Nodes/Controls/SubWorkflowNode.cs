using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record SubWorkflowNode : BaseNode
{
    [JsonPropertyName("config")]
    public SubWorkflowConfig? Config { get; init; }


    [JsonIgnore]
    public override string Kind => NodeKind.ControlSubWorkflow;
}
