using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>Reads workflow definitions. Nothing here changes state.</summary>
public interface IWorkflowQueryService
{
    /// <summary>
    /// Validates a definition without publishing it. Returns every issue - warnings included - so a
    /// builder can surface them while editing rather than discovering them by creating a version.
    /// </summary>
    WorkflowValidationResponse Validate(WorkflowDefinition? definition);

    Task<Result<WorkflowDefinitionDto>> GetCurrentAsync(int workspaceId, Guid refId, CancellationToken ct);

    Task<Result<WorkflowDefinitionDto>> GetVersionAsync(int workspaceId, Guid refId, int version, CancellationToken ct);

    Task<Result<Wbskt.Models.IPagedList<WorkflowSummaryDto>>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct);

    /// <summary>Every version of a workflow, newest first, without definitions.</summary>
    Task<Result<IReadOnlyList<WorkflowVersionDto>>> GetVersionsAsync(int workspaceId, Guid refId, CancellationToken ct);

    Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct);
}
