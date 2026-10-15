using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record EndConfig
{
    /// <summary>
    /// The value this run hands back to the parent that started it as a sub-workflow, evaluated against
    /// the branch that reaches this node and read by the parent as <c>childResult.output</c>. Without it
    /// a parent only learned the child's terminal status, never anything the child computed. When
    /// several branches end at End nodes with an output, the last one to end is what the parent reads.
    /// </summary>
    [JsonPropertyName("output")]
    public WorkflowExpression? Output { get; init; } = null;
}
