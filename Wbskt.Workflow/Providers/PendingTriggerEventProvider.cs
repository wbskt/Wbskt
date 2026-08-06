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

    public async Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.PendingTriggerEvent_Enqueue",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@CorrelationKey", correlationKey);
                p.AddWithValue("@InboundEventJson", inboundEventJson);
            },
            Map,
            new InvalidOperationException("PendingTriggerEvent_Enqueue did not return a row."),
            ct
        );
    }

    public async Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        try
        {
            return await ExecuteSingleAsync(
                "dbo.PendingTriggerEvent_DequeueNext",
                p =>
                {
                    p.AddWithValue("@WorkflowRefId", workflowRefId);
                    p.AddWithValue("@TriggerNodeId", triggerNodeId);
                    p.AddWithValue("@CorrelationKey", correlationKey);
                },
                Map,
                null,
                ct
            );
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.PendingTriggerEvent_DeleteAllBy_RunKey",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@CorrelationKey", correlationKey);
            },
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
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            CorrelationKey = reader.GetString(reader.GetOrdinal("CorrelationKey")),
            InboundEventJson = reader.GetString(reader.GetOrdinal("InboundEventJson")),
            EnqueuedAt = reader.GetDateTime(reader.GetOrdinal("EnqueuedAt")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
