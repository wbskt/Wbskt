using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowRunQueryService
{
    Task<Result<RunListResponse>> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);

    /// <summary>Every run in the workspace, newest first — the "recent activity" view.</summary>
    Task<Result<RunListResponse>> ListByWorkspaceAsync(int workspaceId, string? statusFilter, int top, long? cursorId, CancellationToken ct);
    Task<Result<RunDetailDto>> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct);
    Task<Result> CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct);
    Task<Result<int>> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct);
}
