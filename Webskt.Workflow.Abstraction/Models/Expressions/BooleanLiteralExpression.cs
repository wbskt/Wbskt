using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public class BooleanLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public bool Value { get; set; }
}