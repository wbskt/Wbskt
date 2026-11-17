using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;
using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>
/// Publishes workflows and moves them between their states: Enabled (one version live), Disabled,
/// Deprecated and Deleted.
/// </summary>
public interface IWorkflowLifecycleService
{
    /// <summary>
    /// Publishes a new version and, when the request asks to enable it, makes it live. The two are separate
    /// steps: when the enable is refused the version stays published but not live, the live version is
    /// untouched, and the error says so.
    /// </summary>
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, CancellationToken ct);

    /// <summary>
    /// Publishes with the checks of <paramref name="guards"/>, as publishing a draft does: refused with
    /// 409 <c>WORKFLOW_DRAFT_OUTDATED</c> when the current version is not the one expected.
    /// </summary>
    Task<Result<WorkflowPublishResponse>> PublishAsync(int workspaceId, Guid workspaceRef, WorkflowPublishRequest request, WorkflowPublishGuards guards, CancellationToken ct);

    /// <summary>
    /// Makes any published version the live one, from Enabled (a switch) or Disabled. The version is checked
    /// against today's rules first. Enabling the version already live changes nothing.
    /// </summary>
    Task<Result<WorkflowStateResponse>> EnableAsync(int workspaceId, Guid workspaceRef, Guid refId, int version, CancellationToken ct);

    /// <summary>
    /// Stops new runs and drops the events queued behind a busy run; the workflow stays editable. Runs
    /// going finish, or are cancelled when <paramref name="cancelRunningRuns"/>. Disabling a disabled
    /// workflow changes nothing.
    /// </summary>
    Task<Result<WorkflowStateResponse>> DisableAsync(int workspaceId, Guid refId, bool cancelRunningRuns, CancellationToken ct);

    /// <summary>Retires a workflow: it stops like a disable and becomes read-only until reinstated.</summary>
    Task<Result<WorkflowStateResponse>> DeprecateAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>Brings a deprecated workflow back as Disabled: editable again, live once a version is enabled.</summary>
    Task<Result<WorkflowStateResponse>> ReinstateAsync(int workspaceId, Guid refId, CancellationToken ct);

    /// <summary>
    /// Deletes a workflow: it leaves every list and current-version read, its triggers stop, runs
    /// in flight are cancelled, and its RefId cannot be published again. Past runs stay readable.
    /// </summary>
    Task<Result> DeleteAsync(int workspaceId, Guid refId, CancellationToken ct);
}
