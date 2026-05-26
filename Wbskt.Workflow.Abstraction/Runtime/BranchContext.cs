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
);
