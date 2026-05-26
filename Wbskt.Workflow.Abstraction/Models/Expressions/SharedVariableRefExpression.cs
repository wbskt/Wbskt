using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record SharedVariableRefExpression(
    [property: JsonPropertyName("name")] string Name
) : WorkflowExpression;
