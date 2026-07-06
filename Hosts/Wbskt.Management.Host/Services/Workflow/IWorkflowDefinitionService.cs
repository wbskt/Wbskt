using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowDefinitionService
{
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, WorkflowPublishRequest request, CancellationToken ct);
    Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<Result<WorkflowDefinitionDto>> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct);
    Task<Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct);
    Task<Result> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct);
}
