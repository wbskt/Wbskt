using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IEventLogService
{
    /// <summary>
    /// A page of the workspace's event log matching <paramref name="query"/>, newest first: the page
    /// after <paramref name="cursor"/>, or the first when it is null. <paramref name="take"/> is
    /// clamped to 1..<see cref="Paging.MaxPageSize"/>. A policy or client the workspace does not own
    /// is a 404; an unknown group, a bad range or an over-long search is a 400.
    /// </summary>
    Task<Result<Page<EventLogResponse>>> GetLogsAsync(
        int workspaceId,
        EventLogQuery query,
        long? cursor,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every entry matching <paramref name="query"/>, newest first, as CSV, up to
    /// <see cref="EventLogService.MaxExportRows"/>. The range defaults to the last
    /// <see cref="EventLogService.DefaultRange"/>.
    /// </summary>
    Task<Result<string>> GetCsvAsync(int workspaceId, EventLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many entries each group has over [from, to), which defaults to the last
    /// <see cref="EventLogService.DefaultRange"/>. Device traffic counts toward the total only when
    /// <paramref name="traffic"/> includes it.
    /// </summary>
    Task<Result<EventLogSummaryResponse>> GetSummaryAsync(
        int workspaceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        EventLogTraffic traffic,
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
