using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record BinaryExpression(
    [property: JsonPropertyName("left")] WorkflowExpression Left,
    [property: JsonPropertyName("operator")][property: JsonConverter(typeof(JsonStringEnumConverter))] BinaryOperator Operator,
    [property: JsonPropertyName("right")] WorkflowExpression Right
) : WorkflowExpression;
