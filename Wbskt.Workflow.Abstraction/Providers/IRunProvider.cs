using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IRunProvider
{
    Task<RunRow> CreateAsync(RunRow row, CancellationToken ct);
    Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct);
    Task<RunRow> GetByIdAsync(long runId, CancellationToken ct);
    Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);

    /// <summary>
    /// Every run in a workspace, newest first. Runs carry no workspace of their own - it lives on the
    /// definition - so this joins through WorkflowDefinitionId.
    /// </summary>
    Task<IReadOnlyCollection<RunRow>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct);

    /// <summary>Outcome counts and duration percentiles for runs started in the window.</summary>
    Task<RunStatsRow> GetStatsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>The same figures across every workflow in a workspace. Defaulted so the ~15 test doubles need no stub.</summary>
    Task<RunStatsRow> GetWorkspaceStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        => throw new NotSupportedException();

    /// <summary>
    /// Per-workflow rows behind a workspace rollup, highest volume first. A workspace-wide success rate
    /// is run-weighted, so one busy workflow at 99% hides a quiet one at 0%; this is what makes the
    /// rollup safe to read.
    /// </summary>
    Task<IReadOnlyCollection<WorkflowRunSummaryRow>> GetPerWorkflowStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<WorkflowRunSummaryRow>>(Array.Empty<WorkflowRunSummaryRow>());

    /// <summary>The error codes that actually occur, most frequent first.</summary>
    Task<IReadOnlyCollection<RunFailureBucketRow>> GetTopFailuresAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct);

    /// <summary>Per-node timings, slowest average first.</summary>
    Task<IReadOnlyCollection<NodeTimingRow>> GetNodeTimingsAsync(Guid workflowRefId, DateTime fromUtc, DateTime toUtc, int top, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<RunRow>>(Array.Empty<RunRow>());
    Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct);
    Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct);
    Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct);
    Task<(bool Transitioned, RunRow Run)> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct);
}
