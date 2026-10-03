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

    /// <summary>Every version of a workflow, newest first, without definitions.</summary>
    Task<Result<IReadOnlyList<WorkflowVersionDto>>> GetVersionsAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>
    /// Deletes a workflow: it leaves every list and current-version read, its triggers stop, runs
    /// in flight are cancelled, and its RefId cannot be published again. Past runs stay readable.
    /// </summary>
    Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>
    /// The inverse of <see cref="DeprecateAsync"/>: re-enables the current version and re-registers
    /// its triggers (re-seeding schedules from their cron). Without this, deprecating is one-way and
    /// the only route back is republishing.
    /// </summary>
    Task<Result> ReinstateAsync(int workspaceId, Guid workspaceRef, Guid refId, CancellationToken ct);

    /// <summary>
    /// Republishes an earlier version's definition as a new version. Append-only: the old row is left
    /// alone so the runs of every version keep pointing at the definition they ran.
    /// </summary>
    Task<Result<WorkflowPublishResponse>> RollbackAsync(int workspaceId, Guid workspaceRef, Guid refId, int version, CancellationToken ct);
    Task<Result> EnsureWorkflowInWorkspaceAsync(int workspaceId, Guid workflowRefId, CancellationToken ct);
}
