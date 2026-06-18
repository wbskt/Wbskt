using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IWorkflowDefinitionProvider
{
    Task<int?> FindByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByIdAsync(int id, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetByRefIdVersionAsync(Guid refId, int version, CancellationToken ct);
    Task<WorkflowDefinitionRow> GetCurrentByRefIdAsync(Guid refId, CancellationToken ct);
    Task<WorkflowDefinitionRow> InsertAsync(WorkflowDefinitionRow row, CancellationToken ct);
    Task<(int TotalCount, IReadOnlyCollection<WorkflowDefinitionRow> Items)> GetAllSummariesAsync(int workspaceId, int skip, int take, CancellationToken ct);
    Task DeprecateAsync(int id, CancellationToken ct);
}
