using Wbskt.Foundation.Abstraction;
using Wbskt.Workflow.Entities;

namespace Wbskt.Workflow.Providers;

public interface IWorkflowProvider : IReferenceProvider
{
    // Management Operations
    Task<IReadOnlyCollection<WorkflowEntity>> GetAllByWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default);
    Task<WorkflowEntity> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<WorkflowEntity> InsertAsync(int workspaceId, string name, string? description, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, string name, string description, bool isEnabled, string definitionJson, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    // Engine Operations
    Task<IReadOnlyCollection<WorkflowEntity>> GetAllEnabledAsync(CancellationToken cancellationToken = default);
    Task<WorkflowEntity> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
}
