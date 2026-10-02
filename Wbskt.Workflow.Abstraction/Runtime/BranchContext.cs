using System.Text.Json;

namespace Wbskt.Workflow.Abstraction.Runtime;

public sealed record BranchContext(
    long RunId,
    long BranchId,
    int WorkflowDefinitionId,
    Guid WorkflowDefinitionRefId,
    int Version,
    string CurrentNodeId,
    int Attempt,
    IReadOnlyDictionary<string, JsonElement> LocalState,
    IReadOnlyDictionary<string, JsonElement> TriggerPayload,
    string CorrelationKey,
    DateTime StartedAt,
    int WorkspaceId
)
{
    /// <summary>
    /// The public reference id of the run this branch belongs to. Used by executors that
    /// need to scope external interactions (e.g. AwaitSignal keys its bookmark by run RefId).
    /// </summary>
    public Guid RunRefId { get; init; }

    /// <summary>
    /// The public reference id of the branch.
    /// </summary>
    public Guid BranchRefId { get; init; }

    /// <summary>
    /// Identifies this visit of the branch to its current node: the branch row's version when the
    /// node was picked up. It changes every time the branch moves, so a loop that brings the branch
    /// back to the same node (a sequential ForEach) is a new visit, while a crash-and-recover replay
    /// of an unfinished node is the same one. Empty when the branch was not loaded from storage.
    /// </summary>
    public string VisitToken { get; init; } = string.Empty;
}
