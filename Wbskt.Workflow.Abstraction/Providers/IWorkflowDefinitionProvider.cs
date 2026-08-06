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

    /// <summary>Enables or disables a definition (<c>WorkflowDefinition_UpdateIsEnabled</c>).</summary>
    Task SetEnabledAsync(int id, bool isEnabled, CancellationToken ct);
}
