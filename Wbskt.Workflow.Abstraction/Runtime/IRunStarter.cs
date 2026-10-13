namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunStarter
{
    /// <summary>
    /// Starts a run of the workflow from its trigger node. With a <paramref name="triggerStartKey"/>, a
    /// run already started under the same key is returned instead (<see cref="RunStartResult.Created"/>
    /// false) and nothing new is created. Past the workspace's active-run ceiling it throws
    /// <see cref="WorkspaceRunCeilingException"/>, unless <paramref name="exemptFromCeiling"/>: a start
    /// that takes a place already held (a sub-workflow child, a queued event replayed when its run
    /// finished, a run replacing the ones it cancels).
    /// </summary>
    Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct, bool exemptFromCeiling = false);

    /// <summary>The run an earlier attempt started under <paramref name="triggerStartKey"/>, if any.</summary>
    Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct);
}

public sealed record RunStartResult(long RunId, Guid RunRefId, long BranchId, bool Created);

/// <summary>
/// Thrown by <see cref="IRunStarter.StartAsync"/> when the workflow's workspace already has
/// <paramref name="Limit"/> runs going. Nothing was created.
/// </summary>
public sealed class WorkspaceRunCeilingException(int workspaceId, int limit)
    : InvalidOperationException($"Workspace {workspaceId} already has {limit} runs going, its limit.")
{
    public int WorkspaceId { get; } = workspaceId;

    public int Limit { get; } = limit;
}
