using System.Text.Json.Serialization;
using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public sealed class UnaryExpression : WorkflowExpression
{
    [JsonPropertyName("op")]
    public UnaryOperator Operator { get; set; }

    [JsonPropertyName("operand")]
    public WorkflowExpression Operand { get; set; } = default!;
}
