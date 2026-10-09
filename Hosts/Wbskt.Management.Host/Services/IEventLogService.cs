using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IEventLogService
{
    /// <summary>
    /// A page of the workspace's event log, newest first: the page after <paramref name="cursor"/>,
    /// or the first when it is null. <paramref name="take"/> is clamped to 1..<see cref="Paging.MaxPageSize"/>.
    /// </summary>
    Task<Result<Page<EventLogResponse>>> GetLogsAsync(
        int workspaceId,
        string? eventName,
        EventCriticality? criticality,
        int? policyId,
        int? clientId,
        int? workflowId,
        long? cursor,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>A page of one client's comms entries, paged as <see cref="GetLogsAsync"/>.</summary>
    Task<Result<Page<EventLogResponse>>> GetClientCommsAsync(
        int workspaceId,
        int clientId,
        string? direction,
        long? cursor,
        int take,
        CancellationToken cancellationToken = default);
}
