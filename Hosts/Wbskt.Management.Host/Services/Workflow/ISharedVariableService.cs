using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>Reading and setting a workflow's shared variables from the console.</summary>
public interface ISharedVariableService
{
    /// <summary><c>WORKFLOW_NOT_FOUND</c> for a workflow that is not this workspace's, <c>VARIABLE_NOT_FOUND</c> for a name it has no variable by.</summary>
    Task<Result<SharedVariableDto>> GetAsync(int workspaceId, Guid workflowRefId, string name, CancellationToken ct);

    Task<Result<SharedVariableDto>> SetAsync(int workspaceId, Guid workflowRefId, string name, string valueJson, CancellationToken ct);
}
