using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public sealed class LiteralExpression : WorkflowExpression
{
    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
