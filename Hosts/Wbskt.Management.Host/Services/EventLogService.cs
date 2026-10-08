using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class EventLogService : IEventLogService
{
    private readonly IEventProvider _eventProvider;
    private readonly ILogger<EventLogService> _logger;

    public EventLogService(IEventProvider eventProvider, ILogger<EventLogService> logger)
    {
        _eventProvider = eventProvider;
        _logger = logger;
    }

    public async Task<Result<EventLogListResponse>> GetLogsAsync(int workspaceId, string? eventName, EventCriticality? criticality, int? policyId, int? clientId,
        int? workflowId, long? cursor, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying event logs for WorkspaceId: {WorkspaceId}", workspaceId);

        take = Paging.Take(take);
        // One row more than the page, to learn whether there is a next one.
        var rows = await _eventProvider.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, workflowId, cursor, take + 1, cancellationToken);
        return Result<EventLogListResponse>.Success(ToPage(rows, take));
    }

    public async Task<Result<EventLogListResponse>> GetClientCommsAsync(int workspaceId, int clientId, string? direction, long? cursor, int take,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying comms log for client ID {ClientId} in WorkspaceId: {WorkspaceId}", clientId, workspaceId);

        take = Paging.Take(take);
        var rows = await _eventProvider.GetClientCommsAsync(workspaceId, clientId, direction, cursor, take + 1, cancellationToken);
        return Result<EventLogListResponse>.Success(ToPage(rows, take));
    }

    /// <summary>Trims the probe row; the cursor is the last row shown, and only when the probe found more.</summary>
    internal static EventLogListResponse ToPage(IReadOnlyCollection<EventLogRow> rows, int take)
    {
        var page = rows.Take(take).ToList();
        return new EventLogListResponse
        {
            Items = page.Select(r => r.Entry).ToList(),
            NextCursor = rows.Count > take ? page[^1].Id : null
        };
    }
}
