using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record SubWorkflowConfig
{
    [JsonPropertyName("workflowRefId")]
    public required Guid WorkflowRefId { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

}

