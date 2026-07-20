using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IEventLogService
{
    Task<Result<IPagedList<EventLogResponse>>> GetLogsAsync(
        int workspaceId,
        string? eventName,
        EventCriticality? criticality,
        int? policyId,
        int? clientId,
        int? workflowId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
    Task<Result<IPagedList<EventLogResponse>>> GetClientCommsAsync(
        int workspaceId,
        int clientId,
        string? direction,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
