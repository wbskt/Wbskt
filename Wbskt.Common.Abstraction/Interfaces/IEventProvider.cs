using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Common.Abstraction.Interfaces;

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
}
