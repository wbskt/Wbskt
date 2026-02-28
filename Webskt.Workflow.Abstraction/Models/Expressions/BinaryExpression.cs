using System.Text.Json.Serialization;

namespace Webskt.Workflow.Abstraction.Models.Expressions;

public abstract class BinaryExpression : WorkflowExpression
{
    [JsonPropertyName("left")]
    public WorkflowExpression Left { get; set; } = default!;

    [JsonPropertyName("right")]
    public WorkflowExpression Right { get; set; } = default!;
}