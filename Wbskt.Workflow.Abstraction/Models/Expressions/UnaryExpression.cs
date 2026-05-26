using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record UnaryExpression(
    [property: JsonPropertyName("operator")][property: JsonConverter(typeof(JsonStringEnumConverter))] UnaryOperator Operator,
    [property: JsonPropertyName("operand")] WorkflowExpression Operand
) : WorkflowExpression;
