using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Models.Expressions;

public sealed class FunctionExpression : WorkflowExpression
{
    [JsonPropertyName("fn")]
    public FunctionName Function { get; set; }

    [JsonPropertyName("args")]
    public List<WorkflowExpression> Arguments { get; set; } = new();
}
