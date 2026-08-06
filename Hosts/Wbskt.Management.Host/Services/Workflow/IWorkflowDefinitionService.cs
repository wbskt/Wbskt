using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Host.Services.Workflow;

public interface IWorkflowDefinitionService
{
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct);

    /// <summary>
    /// Validates a definition without publishing it. Returns every issue - warnings included - so a
    /// builder can surface them while editing rather than discovering them by creating a version.
    /// </summary>
    WorkflowValidationResponse Validate(WorkflowDefinition? definition);
    Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<Result<WorkflowDefinitionDto>> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct);
    Task<Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct);
    Task<Result> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);
    Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct);
}
