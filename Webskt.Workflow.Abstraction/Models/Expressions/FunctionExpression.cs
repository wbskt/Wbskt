using System.Text.Json.Serialization;
using Webskt.Workflow.Abstraction.Enums;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public sealed class FunctionExpression : WorkflowExpression
{
    [JsonPropertyName("fn")]
    public FunctionName Function { get; set; }

    [JsonPropertyName("args")]
    public List<WorkflowExpression> Arguments { get; set; } = new();
}
