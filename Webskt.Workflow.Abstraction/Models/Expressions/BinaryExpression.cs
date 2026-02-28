using System.Text.Json.Serialization;
using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public sealed class BinaryExpression : WorkflowExpression
{
    [JsonPropertyName("op")]
    public BinaryOperator Operator { get; set; }

    [JsonPropertyName("left")]
    public WorkflowExpression Left { get; set; } = default!;

    [JsonPropertyName("right")]
    public WorkflowExpression Right { get; set; } = default!;
}
