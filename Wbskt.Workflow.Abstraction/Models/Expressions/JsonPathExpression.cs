using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record JsonPathExpression(
    [property: JsonPropertyName("baseExpression")] WorkflowExpression BaseExpression,
    [property: JsonPropertyName("path")] string Path
) : WorkflowExpression;
