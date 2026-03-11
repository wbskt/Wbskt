using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Services;

public interface IEventLogService
{
    Task<IPagedList<EventLogResponse>> GetLogsAsync(
        int workspaceId, 
        string? eventName, 
        EventCriticality? criticality, 
        int skip, 
        int take, 
        CancellationToken cancellationToken = default);
}
