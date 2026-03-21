using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class EventLogService : IEventLogService
{
    private readonly IEventProvider _eventProvider;

    public EventLogService(IEventProvider eventProvider)
    {
        _eventProvider = eventProvider;
    }

    public async Task<IPagedList<EventLogResponse>> GetLogsAsync(int workspaceId, string? eventName, EventCriticality? criticality, int? policyId, int? clientId,
        int? workflowId, int skip, int take, CancellationToken cancellationToken = default)
    {
        // Simple passthrough to provider for MVP
        return await _eventProvider.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, workflowId, skip, take, cancellationToken);
    }
}
