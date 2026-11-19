using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class PendingTriggerEventProvider : BaseSqlProvider, IPendingTriggerEventProvider
{
    public PendingTriggerEventProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<PendingTriggerEventRow?> EnqueueBehindActiveRunAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.PendingTriggerEvent_Enqueue",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@CorrelationKey", correlationKey);
                p.AddWithValue("@InboundEventJson", inboundEventJson);
            },
            Map,
            ct
        );
    }

    public async Task<PendingTriggerEventRow?> LeaseNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, int leaseSeconds, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.PendingTriggerEvent_LeaseNext",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@CorrelationKey", correlationKey);
                p.AddWithValue("@LeaseSec", leaseSeconds);
            },
            Map,
            ct
        );
    }

    public async Task DeleteByIdAsync(long id, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.PendingTriggerEvent_DeleteById",
            p => p.AddWithValue("@Id", id),
            ct
        );
    }

    public async Task<IReadOnlyCollection<PendingTriggerEventKey>> GetIdleKeysAsync(int batchSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.PendingTriggerEvent_GetIdleKeys",
            p => p.AddWithValue("@BatchSize", batchSize),
            reader => new PendingTriggerEventKey(
                reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
                reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
                reader.GetString(reader.GetOrdinal("CorrelationKey"))),
            ct
        );
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.PendingTriggerEvent_DeleteExpired",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            ct
        );
        return result is int count ? count : Convert.ToInt32(result ?? 0);
    }

    public async Task<long> CountAllAsync(CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.PendingTriggerEvent_CountAll",
            null,
            ct
        );
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }

    internal static PendingTriggerEventRow Map(DbDataReader reader)
    {
        return new PendingTriggerEventRow
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            CorrelationKey = reader.GetString(reader.GetOrdinal("CorrelationKey")),
            InboundEventJson = reader.GetString(reader.GetOrdinal("InboundEventJson")),
            EnqueuedAt = reader.GetUtcDateTime("EnqueuedAt")
        };
    }
}
