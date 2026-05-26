using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed record FunctionExpression(
    [property: JsonPropertyName("function")][property: JsonConverter(typeof(JsonStringEnumConverter))] FunctionName Function,
    [property: JsonPropertyName("arguments")] IReadOnlyCollection<WorkflowExpression> Arguments
) : WorkflowExpression;
