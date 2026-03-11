using Wbskt.Common.Abstraction.Interfaces;
using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Services;

internal sealed class EventLogService : IEventLogService
{
    private readonly IEventProvider _eventProvider;

    public EventLogService(IEventProvider eventProvider)
    {
        _eventProvider = eventProvider;
    }

    public async Task<IPagedList<EventLogResponse>> GetLogsAsync(
        int workspaceId, 
        string? eventName, 
        EventCriticality? criticality, 
        int skip, 
        int take, 
        CancellationToken cancellationToken = default)
    {
        // Simple passthrough to provider for MVP
        return await _eventProvider.GetLogsAsync(workspaceId, eventName, criticality, skip, take, cancellationToken);
    }
}
