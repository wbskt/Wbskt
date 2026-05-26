using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record LiteralExpression(
    [property: JsonPropertyName("value")] object? Value
) : WorkflowExpression;
