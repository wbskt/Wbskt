using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services;

public interface IWorkflowDefinitionService
{
    Task<WorkflowPublishResponse> PublishAsync(WorkflowPublishRequest request, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetCurrentAsync(Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetVersionAsync(Guid refId, int version, CancellationToken ct);
    Task DeprecateAsync(Guid refId, CancellationToken ct);
}
