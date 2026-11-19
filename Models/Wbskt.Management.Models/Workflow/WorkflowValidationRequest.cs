using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// A definition to check without publishing it, in the shape a publish sends, so a designer can send the
/// body it would publish. Whether to make the version live is a publish-time choice and is not asked here.
/// </summary>
/// <param name="CreditBudget">The credits one run may use; null for the engine's default.</param>
public sealed record WorkflowValidationRequest(
    string Name,
    string? Description,
    IReadOnlyCollection<BaseNode> Nodes,
    IReadOnlyCollection<Edge> Edges,
    IReadOnlyCollection<SharedVariableDeclaration>? SharedVariableSchema = null,
    decimal? CreditBudget = null,
    bool FailFast = false)
{
    /// <summary>The workflow the definition is for, when it has one, so a step that runs it is found.</summary>
    public Guid? RefId { get; init; }

    /// <summary>
    /// Deprecated: the earlier body, which nested the whole definition here. Read only when <c>nodes</c>
    /// is left out; dropped in the release after (#1059).
    /// </summary>
    public WorkflowDefinition? Definition { get; init; }
}
