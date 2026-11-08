using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IWorkflowDefinitionProvider
{
    Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct);
    Task<WorkflowDefinitionRow?> FindRowByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow?> FindCurrentByRefIdAsync(Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct) => InsertAsync(row, [], ct);

    /// <summary>
    /// Publishes a version together with the steps of it that send through an integration. Throws
    /// <c>SqlException</c> 50023 when one of those integrations is not the workspace's (any more).
    /// </summary>
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs, CancellationToken ct);

    /// <summary>
    /// As above, and a first version is refused with <c>SqlException</c> 50024 when the workspace
    /// already has <paramref name="maxWorkflows"/> workflows. Null is no ceiling.
    /// </summary>
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs, int? maxWorkflows, CancellationToken ct) =>
        InsertAsync(row, integrationRefs, ct);

    /// <summary>
    /// As above, with the checks of <paramref name="guards"/>: <c>SqlException</c> 50025 when the current
    /// version is not the one expected, and the draft deleted with the publish when one is named.
    /// </summary>
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs, int? maxWorkflows, WorkflowPublishGuards guards, CancellationToken ct) =>
        InsertAsync(row, integrationRefs, maxWorkflows, ct);
    /// <summary>
    /// Writes the frozen copy a test run starts from (<c>WorkflowDefinition_InsertTestCopy</c>): loaded by
    /// Id like a version, invisible to every version lookup. <c>Version</c> is the version it is based on.
    /// Throws <c>SqlException</c> 50021 for another workspace's workflow and 50022 for a deleted one.
    /// </summary>
    Task<WorkflowDefinitionRow> InsertTestCopyAsync(WorkflowDefinitionRow row, CancellationToken ct) =>
        throw new NotSupportedException("This provider does not write test copies.");

    Task<IPagedList<WorkflowDefinitionRow>> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct);
    Task DeprecateAsync(int id, CancellationToken ct);

    /// <summary>
    /// Removes a definition row that nothing references. Compensation for a publish that inserted the
    /// row and then failed before its triggers were registered; a definition with runs against it is
    /// history and is never deleted. Returns true when the row was removed.
    /// </summary>
    Task<bool> DeleteUnreferencedAsync(int id, CancellationToken ct);

    /// <summary>
    /// Every version of a workflow in <paramref name="workspaceId"/>, newest first. Empty when the
    /// workflow is unknown, in another workspace, or deleted.
    /// </summary>
    Task<IReadOnlyCollection<WorkflowVersionRow>> GetVersionsAsync(Guid refId, int workspaceId, CancellationToken ct);

    /// <summary>
    /// Deletes a workflow (<c>WorkflowDefinition_Delete</c>): tombstones its RefId, disables every
    /// version and removes its triggers. Null when there was nothing in <paramref name="workspaceId"/>
    /// to delete; otherwise the runs to cancel and the definitions to evict from caches.
    /// </summary>
    Task<WorkflowDeletion?> DeleteAsync(Guid refId, int workspaceId, int deletedBy, CancellationToken ct);

    /// <summary>Enables or disables a definition (<c>WorkflowDefinition_UpdateIsEnabled</c>).</summary>
    Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct);
}
