using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>Publishes, deprecates, reinstates, rolls back and deletes workflows.</summary>
public interface IWorkflowLifecycleService
{
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct);

    /// <summary>
    /// Publishes with the checks of <paramref name="guards"/>, as publishing a draft does: refused with
    /// 409 <c>WORKFLOW_DRAFT_OUTDATED</c> when the current version is not the one expected.
    /// </summary>
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, WorkflowPublishGuards guards, CancellationToken ct);

    Task<Result> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);

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
}
