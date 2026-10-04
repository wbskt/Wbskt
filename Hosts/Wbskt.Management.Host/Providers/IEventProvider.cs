using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Providers;

public interface IEventProvider
{
    Task<int> GetOrInsertEventIdAsync(string eventName, short criticality, CancellationToken cancellationToken = default);
    Task InsertBatchAsync(System.Data.DataTable logs, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="batchSize"/> entries older than the cutoff; returns how many went.</summary>
    Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> entries, newest first, older than <paramref name="cursorId"/> when given.</summary>
    Task<IReadOnlyCollection<EventLogRow>> GetLogsAsync(
        int workspaceId,
        string? eventName,
        EventCriticality? criticality,
        int? policyId,
        int? clientId,
        int? workflowId,
        long? cursorId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> comms entries, newest first, older than <paramref name="cursorId"/> when given.</summary>
    Task<IReadOnlyCollection<EventLogRow>> GetClientCommsAsync(
        int workspaceId,
        int clientId,
        string? direction,
        long? cursorId,
        int take,
        CancellationToken cancellationToken = default);
}
