using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowRunQueryService
{
    Task<RunListResponse> ListByWorkflowAsync(int workspaceId, Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);
    Task<RunDetailDto> GetDetailAsync(int workspaceId, Guid runRefId, CancellationToken ct);
    Task CancelAsync(int workspaceId, Guid runRefId, string reason, CancellationToken ct);

    /// <summary>
    /// Verifies the run belongs to the given workspace and returns its internal id.
    /// Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> if it does not.
    /// </summary>
    Task<int> EnsureRunInWorkspaceAsync(int workspaceId, Guid runRefId, CancellationToken ct);
}
