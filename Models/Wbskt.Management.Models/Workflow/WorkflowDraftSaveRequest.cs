using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Saves a draft, in the shape a publish sends. <c>ExpectedRevision</c> is the revision last read, or null
/// to start a draft; a draft changed since is refused with 409 <c>WORKFLOW_DRAFT_CONFLICT</c>. An invalid
/// definition is still saved.
/// </summary>
/// <param name="CreditBudget">The credits one run may use; null for the engine's default.</param>
public record WorkflowDraftSaveRequest(
    string? Name,
    string? Description,
    IReadOnlyCollection<BaseNode>? Nodes,
    IReadOnlyCollection<Edge>? Edges,
    int? ExpectedRevision = null,
    IReadOnlyCollection<SharedVariableDeclaration>? SharedVariableSchema = null,
    decimal? CreditBudget = null,
    bool FailFast = false)
{
    /// <summary>
    /// Deprecated: the earlier body, which nested the steps here beside the name and description again
    /// and fields the server sets. Read only when <c>nodes</c> is left out; dropped in the release after
    /// (#1059).
    /// </summary>
    public WorkflowDefinition? Definition { get; init; }
}
