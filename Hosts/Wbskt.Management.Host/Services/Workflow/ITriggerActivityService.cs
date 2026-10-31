using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

public interface ITriggerActivityService
{
    /// <summary>
    /// The latest outcomes of one trigger node of a workflow this workspace owns, with its hold state.
    /// <c>WORKFLOW_NOT_FOUND</c> (404) for a workflow that is unknown, deleted or another workspace's.
    /// </summary>
    Task<Result<TriggerActivityResponse>> GetRecentAsync(int workspaceId, Guid workflowRefId, Guid triggerNodeId, CancellationToken ct);
}
