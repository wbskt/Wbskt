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

        try
        {
            take = Paging.Take(take);
            // One row more than the page, to learn whether there is a next one.
            var rows = await _eventProvider.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, workflowId, cursor, take + 1, cancellationToken);
            return Result<EventLogListResponse>.Success(ToPage(rows, take));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query event logs for WorkspaceId: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetLogsAsync exception stack trace for WorkspaceId {WorkspaceId}", workspaceId);
            return Result<EventLogListResponse>.Failure(Error.Failure("EVENT_LOG_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<EventLogListResponse>> GetClientCommsAsync(int workspaceId, int clientId, string? direction, long? cursor, int take,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying comms log for client ID {ClientId} in WorkspaceId: {WorkspaceId}", clientId, workspaceId);

        try
        {
            take = Paging.Take(take);
            var rows = await _eventProvider.GetClientCommsAsync(workspaceId, clientId, direction, cursor, take + 1, cancellationToken);
            return Result<EventLogListResponse>.Success(ToPage(rows, take));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query comms log for client ID {ClientId}. Error: {Message}", clientId, ex.Message);
            _logger.LogTrace(ex, "GetClientCommsAsync exception stack trace for ClientId {ClientId}", clientId);
            return Result<EventLogListResponse>.Failure(Error.Failure("EVENT_LOG_QUERY_ERROR", ex.Message));
        }
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
