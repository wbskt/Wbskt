using System.Text.Json.Serialization;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.SharedVariables;

namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// Publishes a new version of what the author owns: the workflow's name and description, its steps and
/// edges, its shared variables, the credits one run may use and whether a failed branch fails the run.
/// The server sets the rest: the version, when it was published and by whom. <c>Enable</c> is required:
/// true makes the version live as it is published; false leaves the live version (or a disabled
/// workflow) as it is. A first publish without it is Disabled.
/// </summary>
/// <param name="CreditBudget">The credits one run may use; null for the engine's default.</param>
public record WorkflowPublishRequest(
    Guid RefId,
    string Name,
    string? Description,
    IReadOnlyCollection<BaseNode>? Nodes,
    IReadOnlyCollection<Edge>? Edges,
    [property: JsonRequired] bool Enable,
    IReadOnlyCollection<SharedVariableDeclaration>? SharedVariableSchema = null,
    decimal? CreditBudget = null,
    bool FailFast = false)
{
    /// <summary>
    /// Deprecated: the earlier body, which nested the steps here beside the id, name and description
    /// again and fields the server sets. Read only when <c>nodes</c> is left out. Dropped only once the
    /// console sends the flat body (wbskt/Wbskt.Console#229), not simply in the release after (#1059).
    /// </summary>
    public WorkflowDefinition? Definition { get; init; }
}
