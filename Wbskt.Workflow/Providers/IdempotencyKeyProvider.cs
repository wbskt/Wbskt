using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class IdempotencyKeyProvider : BaseSqlProvider, IIdempotencyKeyProvider
{
    public IdempotencyKeyProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.IdempotencyKey_Upsert_Pending",
            p =>
            {
                p.AddWithValue("@KeyValue", keyValue);
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@BranchRefId", branchRefId);
                p.AddWithValue("@NodeId", nodeId);
                p.AddWithValue("@Attempt", attempt);
            },
            Map,
            new InvalidOperationException("IdempotencyKey_Upsert_Pending did not return a row."),
            ct
        );
    }

    public async Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.IdempotencyKey_GetBy_Key",
            p => p.AddWithValue("@KeyValue", keyValue),
            Map,
            new KeyNotFoundException($"IdempotencyKey with KeyValue={keyValue} not found."),
            ct
        );
    }

    public async Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.IdempotencyKey_MarkSucceeded",
            p =>
            {
                p.AddWithValue("@KeyValue", keyValue);
                p.AddWithValue("@ResultJson", resultJson);
            },
            Map,
            new InvalidOperationException("IdempotencyKey_MarkSucceeded did not return a row."),
            ct
        );
    }

    public async Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.IdempotencyKey_MarkFailed",
            p =>
            {
                p.AddWithValue("@KeyValue", keyValue);
                p.AddWithValue("@ErrorJson", errorJson);
            },
            Map,
            new InvalidOperationException("IdempotencyKey_MarkFailed did not return a row."),
            ct
        );
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.IdempotencyKey_DeleteExpired",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
            },
            ct
        );
        return result is int count ? count : Convert.ToInt32(result ?? 0);
    }

    internal static IdempotencyKeyRow Map(DbDataReader reader)
    {
        return new IdempotencyKeyRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            KeyValue = reader.GetString(reader.GetOrdinal("KeyValue")),
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            BranchRefId = reader.GetGuid(reader.GetOrdinal("BranchRefId")),
            NodeId = reader.GetGuid(reader.GetOrdinal("NodeId")),
            Attempt = reader.GetInt32(reader.GetOrdinal("Attempt")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            ResultJson = reader.IsDBNull(reader.GetOrdinal("ResultJson")) ? null : reader.GetString(reader.GetOrdinal("ResultJson")),
            ErrorJson = reader.IsDBNull(reader.GetOrdinal("ErrorJson")) ? null : reader.GetString(reader.GetOrdinal("ErrorJson")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt"))
        };
    }
}
