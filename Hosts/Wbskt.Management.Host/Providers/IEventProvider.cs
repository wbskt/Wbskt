using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Providers;

public interface IEventProvider
{
    Task<int> GetOrInsertEventIdAsync(string eventName, short criticality, CancellationToken cancellationToken = default);
    Task InsertBatchAsync(System.Data.DataTable logs, CancellationToken cancellationToken = default);

    /// <summary>Writes device traffic (events marked <c>[DeviceTraffic]</c>) to dbo.DeviceTraffic; same table type as <see cref="InsertBatchAsync"/>.</summary>
    Task InsertDeviceTrafficBatchAsync(System.Data.DataTable logs, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="batchSize"/> entries older than the cutoff, only of the given events when <paramref name="eventIds"/> is set.</summary>
    Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, IReadOnlyCollection<int>? eventIds = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes device traffic older than the cutoff from among the <paramref name="batchSize"/> oldest rows.</summary>
    Task<int> DeleteDeviceTrafficBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> entries matching <paramref name="filter"/>, newest first, older than <paramref name="cursorId"/> when given.</summary>
    Task<IReadOnlyCollection<EventLogRow>> GetLogsAsync(
        int workspaceId,
        EventLogFilter filter,
        long? cursorId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many entries the workspace logged in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), by event, user and source.</summary>
    Task<IReadOnlyCollection<EventLogCount>> CountAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, bool includeDeviceTraffic = true, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> comms entries, newest first, older than <paramref name="cursorId"/> when given.</summary>
    Task<IReadOnlyCollection<EventLogRow>> GetClientCommsAsync(
        int workspaceId,
        int clientId,
        string? direction,
        long? cursorId,
        int take,
        CancellationToken cancellationToken = default);
}
