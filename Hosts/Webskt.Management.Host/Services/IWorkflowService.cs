using Webskt.Management.Host.Models;
using Webskt.Workflow.Abstraction.Models;

namespace Webskt.Management.Host.Services;

public interface IWorkflowService
{
    Task<IReadOnlyCollection<WorkflowSummaryResponse>> GetAllAsync(int workspaceId, CancellationToken cancellationToken = default);
    
    Task<WorkflowDefinition> GetByIdAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
    
    Task<WorkflowSummaryResponse> CreateAsync(int workspaceId, CreateWorkflowRequest request, CancellationToken cancellationToken = default);
    
    Task UpdateAsync(int workspaceId, int id, UpdateWorkflowRequest request, CancellationToken cancellationToken = default);
    
    Task DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
}
