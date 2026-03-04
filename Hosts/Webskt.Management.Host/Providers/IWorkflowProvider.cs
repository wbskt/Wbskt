using Webskt.Common.Abstraction.Interfaces;
using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Providers;

public interface IWorkflowProvider : IReferenceProvider
{
    Task<IReadOnlyCollection<Models.Workflow>> GetAllByWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default);
    Task<Models.Workflow> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Models.Workflow> InsertAsync(int workspaceId, CreateWorkflowRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, string name, string description, bool isEnabled, string definitionJson, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
