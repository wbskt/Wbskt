using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record TemplateExpression(
    [property: JsonPropertyName("template")] string Template
) : WorkflowExpression;
