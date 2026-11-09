using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Abstraction.Runtime;

public interface IRunStarter
{
    /// <summary>
    /// Starts a run of the workflow from its trigger node. With a <paramref name="triggerStartKey"/>, a
    /// run already started under the same key is returned instead (<see cref="RunStartResult.Created"/>
    /// false) and nothing new is created. Past the plan's limit of active runs it throws
    /// <see cref="PlanRunLimitException"/>, and once the plan's credits for the month are spent
    /// <see cref="PlanCreditLimitException"/> (or <see cref="WorkspaceCreditCapException"/> for the
    /// workspace's own cap), unless <paramref name="exemptFromCeiling"/>: a start
    /// that takes a place already held (a sub-workflow child, a queued event replayed when its run
    /// finished, a run replacing the ones it cancels). With an <paramref name="activeRunGuard"/>, a run
    /// still active for the same trigger and correlation key refuses the start with
    /// <see cref="ActiveRunConflictException"/>.
    /// </summary>
    Task<RunStartResult> StartAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, string? triggerStartKey, CancellationToken ct, bool exemptFromCeiling = false, ActiveRunGuard activeRunGuard = ActiveRunGuard.None);

    /// <summary>
    /// Starts a test run: a run like any other, marked as a test, from a test copy of the definition.
    /// It skips the trigger's concurrency policy but not the plan's limit of active runs, and refuses with
    /// <see cref="TestRunActiveException"/> while another test run of the workflow is going.
    /// </summary>
    Task<RunStartResult> StartTestAsync(int workflowDefinitionId, string triggerNodeId, InboundEvent triggerEvent, CancellationToken ct) =>
        throw new NotSupportedException("This run starter does not start test runs.");

    /// <summary>The run an earlier attempt started under <paramref name="triggerStartKey"/>, if any.</summary>
    Task<RunStartResult?> FindStartedAsync(string triggerStartKey, CancellationToken ct);
}

public sealed record RunStartResult(long RunId, Guid RunRefId, long BranchId, bool Created);

/// <summary>
/// Thrown by <see cref="IRunStarter.StartAsync"/> when the plan of the workflow's workspace already has
/// <paramref name="limit"/> runs going across its tenant's workspaces. Nothing was created.
/// </summary>
public sealed class PlanRunLimitException(int workspaceId, int limit)
    : InvalidOperationException($"The plan of workspace {workspaceId} already has {limit} runs going, its limit.")
{
    public int WorkspaceId { get; } = workspaceId;

    public int Limit { get; } = limit;
}

/// <summary>
/// Thrown by <see cref="IRunStarter.StartAsync"/> when the tenant of the workflow's workspace has spent
/// <paramref name="allowance"/>, its plan's workflow credits for this calendar month. Nothing was created.
/// </summary>
public sealed class PlanCreditLimitException(int workspaceId, decimal allowance)
    : InvalidOperationException($"The plan of workspace {workspaceId} has spent its {allowance} workflow credits for this month.")
{
    public int WorkspaceId { get; } = workspaceId;

    public decimal Allowance { get; } = allowance;
}

/// <summary>
/// Thrown by <see cref="IRunStarter.StartAsync"/> when the workflow's workspace has spent its own monthly
/// credit cap, though its tenant may have credits left. Nothing was created.
/// </summary>
public sealed class WorkspaceCreditCapException(int workspaceId)
    : InvalidOperationException($"Workspace {workspaceId} has spent its monthly credit cap.")
{
    public int WorkspaceId { get; } = workspaceId;
}

/// <summary>Thrown by <see cref="IRunStarter.StartTestAsync"/> while another test run of the workflow is going. Nothing was created.</summary>
public sealed class TestRunActiveException(Guid workflowRefId)
    : InvalidOperationException($"A test run of workflow '{workflowRefId}' is already going.")
{
    public Guid WorkflowRefId { get; } = workflowRefId;
}

/// <summary>
/// Thrown by <see cref="IRunStarter.StartAsync"/> when its <see cref="ActiveRunGuard"/> finds a run
/// already active for the trigger and correlation key, which another event started after the
/// concurrency policy read the active runs. Nothing was created; the caller decides again.
/// </summary>
public sealed class ActiveRunConflictException(string correlationKey)
    : InvalidOperationException($"A run is already active for correlation key '{correlationKey}'.")
{
    public string CorrelationKey { get; } = correlationKey;
}
