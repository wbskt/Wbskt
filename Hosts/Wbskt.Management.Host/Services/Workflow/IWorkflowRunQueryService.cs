using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowRunQueryService
{
    Task<Result<RunListResponse>> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);

    /// <summary>Every run in the workspace, newest first — the "recent activity" view.</summary>
    Task<Result<RunListResponse>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct);

    /// <summary>
    /// How a workflow is doing over a window: outcome counts, duration percentiles, success rate, the
    /// error codes that actually occur, and which nodes are slowest.
    /// </summary>
    Task<Result<WorkflowStatsResponse>> GetStatsAsync(int workspaceId, Guid workflowRefId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>The same question across a whole workspace, plus the per-workflow rows that keep the
    /// run-weighted rate honest.</summary>
    Task<Result<WorkspaceStatsResponse>> GetWorkspaceStatsAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<Result<RunDetailDto>> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct);
    Task<Result> CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct);
    Task<Result<int>> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct);
}
