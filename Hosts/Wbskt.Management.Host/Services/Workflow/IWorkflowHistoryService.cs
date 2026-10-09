using Wbskt.Infrastructure;
using Wbskt.Management.Models.Workflow;

namespace Wbskt.Management.Host.Services.Workflow;

/// <summary>A run's history events, oldest first, for the console's run timeline.</summary>
public interface IWorkflowHistoryService
{
    /// <summary>
    /// A page of events after <paramref name="fromEventId"/>; <c>NextCursor</c> is set only when another
    /// page exists. <c>RUN_NOT_FOUND</c> for a run that is unknown or another workspace's.
    /// </summary>
    Task<Result<HistoryListResponse>> ListAsync(int workspaceId, Guid runRefId, long fromEventId, int top, CancellationToken ct);
}
