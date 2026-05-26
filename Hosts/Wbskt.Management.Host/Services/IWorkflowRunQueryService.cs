using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services;

public interface IWorkflowRunQueryService
{
    Task<RunListResponse> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct);
    Task<RunDetailDto> GetDetailAsync(Guid runRefId, CancellationToken ct);
    Task CancelAsync(Guid runRefId, string reason, CancellationToken ct);
}
