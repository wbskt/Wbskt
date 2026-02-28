using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public class NumberLiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public double Value { get; set; }
}