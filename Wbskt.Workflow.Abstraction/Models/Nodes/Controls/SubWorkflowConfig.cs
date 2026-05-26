using System.Text.Json.Serialization;
namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
public sealed record SubWorkflowConfig(
    [property: JsonPropertyName("workflowRefId")] Guid WorkflowRefId,
    [property: JsonPropertyName("correlationKey")] string? CorrelationKey = null);
