using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowDefinitionService
{
    Task<WorkflowPublishResponse> PublishAsync(int workspaceId, WorkflowPublishRequest request, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionDto> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct);
    Task DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>
    /// Verifies the workflow belongs to the given workspace.
    /// Throws <see cref="Wbskt.Primitives.Exceptions.SecurityException"/> if it does not.
    /// </summary>
    Task EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct);
}
