namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunStarter
{
    /// <summary>
    /// Starts a run of the workflow from its trigger node. With a <paramref name="triggerStartKey"/>, a
    /// run already started under the same key is returned instead (<see cref="RunStartResult.Created"/>
    /// false) and nothing new is created.
    /// </summary>
    Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct);

    /// <summary>The run an earlier attempt started under <paramref name="triggerStartKey"/>, if any.</summary>
    Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct);
}

public sealed record RunStartResult(long RunId, Guid RunRefId, long BranchId, bool Created);
