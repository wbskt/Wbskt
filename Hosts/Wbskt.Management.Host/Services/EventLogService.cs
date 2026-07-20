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

    public async Task<Result<IPagedList<EventLogResponse>>> GetLogsAsync(int workspaceId, string? eventName, EventCriticality? criticality, int? policyId, int? clientId,
        int? workflowId, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying event logs for WorkspaceId: {WorkspaceId}", workspaceId);

        try
        {
            var logs = await _eventProvider.GetLogsAsync(workspaceId, eventName, criticality, policyId, clientId, workflowId, skip, take, cancellationToken);
            _logger.LogTrace("Retrieved {Count} event logs for WorkspaceId: {WorkspaceId}", logs.TotalCount, workspaceId);
            return Result<IPagedList<EventLogResponse>>.Success(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query event logs for WorkspaceId: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetLogsAsync exception stack trace for WorkspaceId {WorkspaceId}", workspaceId);
            return Result<IPagedList<EventLogResponse>>.Failure(Error.Failure("EVENT_LOG_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<IPagedList<EventLogResponse>>> GetClientCommsAsync(int workspaceId, int clientId, string? direction, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying comms log for client ID {ClientId} in WorkspaceId: {WorkspaceId}", clientId, workspaceId);

        try
        {
            var logs = await _eventProvider.GetClientCommsAsync(workspaceId, clientId, direction, skip, take, cancellationToken);
            return Result<IPagedList<EventLogResponse>>.Success(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query comms log for client ID {ClientId}. Error: {Message}", clientId, ex.Message);
            _logger.LogTrace(ex, "GetClientCommsAsync exception stack trace for ClientId {ClientId}", clientId);
            return Result<IPagedList<EventLogResponse>>.Failure(Error.Failure("EVENT_LOG_QUERY_ERROR", ex.Message));
        }
    }
}
