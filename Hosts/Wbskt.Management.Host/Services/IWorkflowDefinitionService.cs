using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services;

public interface IWorkflowDefinitionService
{
    Task<WorkflowPublishResponse> PublishAsync(int workspaceId, WorkflowPublishRequest request, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct);
    Task DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);
}
