using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models.Expressions;

namespace Wbskt.Workflow.Abstraction.Models.Nodes.Controls;

public sealed record SubWorkflowConfig
{
    [JsonPropertyName("workflowRefId")]
    public required Guid WorkflowRefId { get; init; }

    [JsonPropertyName("correlationKey")]
    public string? CorrelationKey { get; init; } = null;

    /// <summary>
    /// Values passed to the child run, each evaluated against the parent branch and merged into the
    /// child's <c>$trigger.body</c>. Without this a child always receives the same fixed payload, which
    /// makes a shared sub-workflow useless as anything but a fixed subroutine.
    /// </summary>
    /// <remarks>
    /// <c>parentRunRefId</c> and <c>correlationKey</c> are reserved: the completion hook needs the first
    /// to find its way home, so an input of either name is rejected at publish rather than silently
    /// overwriting it.
    /// </remarks>
    [JsonPropertyName("input")]
    public IReadOnlyDictionary<string, WorkflowExpression>? Input { get; init; } = null;

    /// <summary>Keys the engine writes into the child's body itself; an author may not supply them.</summary>
    public static readonly IReadOnlySet<string> ReservedInputKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parentRunRefId", "correlationKey" };
}

