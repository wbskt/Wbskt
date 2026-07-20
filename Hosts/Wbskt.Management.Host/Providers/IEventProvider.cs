using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Providers;

public interface IEventProvider
{
    Task<int> GetOrInsertEventIdAsync(string eventName, short criticality, CancellationToken cancellationToken = default);
    Task InsertBatchAsync(System.Data.DataTable logs, CancellationToken cancellationToken = default);
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
    Task<IPagedList<EventLogResponse>> GetClientCommsAsync(
        int workspaceId,
        int clientId,
        string? direction,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
