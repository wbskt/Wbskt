using Wbskt.Models;
using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IWorkflowDefinitionProvider
{
    Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct);
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
