using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Abstraction.Providers;

/// <summary>
/// Moves a published workflow between its states. Each call is one transaction under the workflow's
/// row lock, and the procedures are the only place the legal transitions are decided: a refused one
/// throws a <c>SqlException</c> whose number <c>WorkflowLifecycleErrors</c> maps (50040 not found,
/// 50041 deprecated, 50042 not deprecated, 50043 no such version, 50023 integration gone).
/// </summary>
public interface IWorkflowStateProvider
{
    /// <summary>Null when the workflow is unknown, another workspace's, deleted, or only a draft.</summary>
    Task<WorkflowStateRow?> FindAsync(Guid refId, int workspaceId, CancellationToken ct);

    /// <summary>
    /// Makes <paramref name="version"/> the live one, swapping in <paramref name="plan"/>'s registrations
    /// and schedules and dropping events queued for a trigger the version does not have.
    /// </summary>
    Task<WorkflowEnableResult> EnableAsync(Guid refId, int workspaceId, int version, int changedBy, TriggerPlan plan, IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs, CancellationToken ct);

    /// <summary>Takes the workflow out of service: Disabled, or Deprecated when <paramref name="deprecate"/>.</summary>
    Task<WorkflowDisableResult> DisableAsync(Guid refId, int workspaceId, int changedBy, bool deprecate, CancellationToken ct);

    /// <summary>Brings a deprecated workflow back as Disabled.</summary>
    Task ReinstateAsync(Guid refId, int workspaceId, int changedBy, CancellationToken ct);
}
