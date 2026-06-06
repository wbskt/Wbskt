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
    DateTime StartedAt
)
{
    /// <summary>
    /// The public reference id of the run this branch belongs to. Used by executors that
    /// need to scope external interactions (e.g. AwaitSignal keys its bookmark by run RefId).
    /// </summary>
    public Guid RunRefId { get; init; }
}
