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
    private readonly string _connectionString;

    public IdempotencyKeyProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<IdempotencyKeyRow> UpsertPendingAsync(string keyValue, int runId, Guid branchRefId, Guid nodeId, int attempt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.IdempotencyKey_Upsert_Pending", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@KeyValue", keyValue);
        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@BranchRefId", branchRefId);
        command.Parameters.AddWithValue("@NodeId", nodeId);
        command.Parameters.AddWithValue("@Attempt", attempt);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("IdempotencyKey_Upsert_Pending did not return a row.");
    }

    public async Task<IdempotencyKeyRow> GetByKeyAsync(string keyValue, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.IdempotencyKey_GetBy_Key", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@KeyValue", keyValue);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"IdempotencyKey with KeyValue={keyValue} not found.");
    }

    public async Task<IdempotencyKeyRow> MarkSucceededAsync(string keyValue, string resultJson, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.IdempotencyKey_MarkSucceeded", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@KeyValue", keyValue);
        command.Parameters.AddWithValue("@ResultJson", resultJson);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("IdempotencyKey_MarkSucceeded did not return a row.");
    }

    public async Task<IdempotencyKeyRow> MarkFailedAsync(string keyValue, string errorJson, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.IdempotencyKey_MarkFailed", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@KeyValue", keyValue);
        command.Parameters.AddWithValue("@ErrorJson", errorJson);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("IdempotencyKey_MarkFailed did not return a row.");
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.IdempotencyKey_DeleteExpired", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@CutoffUtc", cutoffUtc);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        await connection.OpenAsync(ct);
        object? result = await command.ExecuteScalarAsync(ct);
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
