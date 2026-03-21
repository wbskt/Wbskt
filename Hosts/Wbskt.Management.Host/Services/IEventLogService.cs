using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IEventLogService
{
    Task<IPagedList<EventLogResponse>> GetLogsAsync(
        int workspaceId, 
        string? eventName, 
        EventCriticality? criticality, 
        int? policyId,
        int? clientId,
        int? workflowId,
        int skip, 
        int take, 
        CancellationToken cancellationToken = default);
}
