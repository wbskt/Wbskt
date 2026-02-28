using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public class StringLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}