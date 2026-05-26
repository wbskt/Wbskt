using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record BranchStateRefExpression(
    [property: JsonPropertyName("path")] string Path
) : WorkflowExpression;
