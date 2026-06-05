using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class RunProvider : BaseSqlProvider, IRunProvider
{
    private readonly string _connectionString;

    public RunProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<RunRow> CreateAsync(RunRow row, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_Create", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", row.RefId);
        command.Parameters.AddWithValue("@WorkflowDefinitionId", row.WorkflowDefinitionId);
        command.Parameters.AddWithValue("@WorkflowRefId", row.WorkflowRefId);
        command.Parameters.AddWithValue("@WorkflowVersion", row.WorkflowVersion);
        command.Parameters.AddWithValue("@TriggerNodeId", row.TriggerNodeId);
        command.Parameters.AddWithValue("@CorrelationKey", (object?)row.CorrelationKey ?? DBNull.Value);
        command.Parameters.AddWithValue("@StartedAt", row.StartedAt);
        command.Parameters.AddWithValue("@CreditBudget", row.CreditBudget);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("Run_Create did not return a row.");
    }

    public async Task<int?> FindByRefIdAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_FindBy_RefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return reader.GetInt32(0);
        }

        return null;
    }

    public async Task<RunRow> GetByIdAsync(long runId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            SELECT
                Id,
                RefId,
                WorkflowDefinitionId,
                WorkflowRefId,
                WorkflowVersion,
                TriggerNodeId,
                CorrelationKey,
                Status,
                StartedAt,
                CompletedAt,
                CancellationRequestedAt,
                CancellationReason,
                CreditBudget,
                CreatedAt
            FROM dbo.Runs
            WHERE Id = @Id;
            """,
            connection);
        command.CommandType = CommandType.Text;
        command.Parameters.AddWithValue("@Id", checked((int)runId));

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Run with Id={runId} not found.");
    }

    public async Task<RunRow> GetByRefIdAsync(Guid refId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_GetBy_RefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Run with RefId={refId} not found.");
    }

    public async Task<IReadOnlyCollection<RunRow>> ListByWorkflowAsync(Guid workflowRefId, string? statusFilter, int top, long? cursorId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_ListBy_WorkflowRefId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@StatusFilter", (object?)statusFilter ?? DBNull.Value);
        command.Parameters.AddWithValue("@Top", top);
        command.Parameters.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RunRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<RunRow>> GetActiveByWorkflowRefIdCorrelationKeyAsync(Guid workflowRefId, string correlationKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_GetActiveBy_WorkflowRefId_CorrelationKey", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@CorrelationKey", correlationKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RunRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<RunRow>> GetActiveByCorrelationAsync(int workflowDefinitionId, string correlationKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_GetActiveBy_Correlation", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);
        command.Parameters.AddWithValue("@CorrelationKey", correlationKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RunRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<RunRow> UpdateStatusAsync(Guid refId, string status, DateTime? completedAt, DateTime? cancellationRequestedAt, string? cancellationReason, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_UpdateStatus", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@CompletedAt", (object?)completedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@CancellationRequestedAt", (object?)cancellationRequestedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@CancellationReason", (object?)cancellationReason ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Run with RefId={refId} not found.");
    }

    public async Task<IReadOnlyCollection<RunRow>> GetStuckRunsAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_GetStuck", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@CutoffUtc", cutoffUtc);
        command.Parameters.AddWithValue("@BatchSize", batchSize);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<RunRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<long> CountByStatusAsync(string status, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_CountByStatus", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Status", status);

        await connection.OpenAsync(ct);
        object? result = await command.ExecuteScalarAsync(ct);
        return result is long count ? count : Convert.ToInt64(result ?? 0L);
    }

    public async Task<bool> TransitionStatusAsync(long runId, string fromStatus, string toStatus, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_TransitionStatus", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", checked((int)runId));
        command.Parameters.AddWithValue("@FromStatus", fromStatus);
        command.Parameters.AddWithValue("@ToStatus", toStatus);

        await connection.OpenAsync(ct);
        object? result = await command.ExecuteScalarAsync(ct);
        return result is int rowsAffected && rowsAffected > 0;
    }

    public async Task<RunRow> SetTerminalAsync(long runId, string status, DateTime completedAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.Run_SetTerminal", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", checked((int)runId));
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@CompletedAt", completedAt);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new KeyNotFoundException($"Run with Id={runId} not found.");
    }

    internal static RunRow Map(DbDataReader reader)
    {
        return new RunRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            WorkflowVersion = reader.GetInt32(reader.GetOrdinal("WorkflowVersion")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            CorrelationKey = reader.IsDBNull(reader.GetOrdinal("CorrelationKey")) ? null : reader.GetString(reader.GetOrdinal("CorrelationKey")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            StartedAt = reader.GetDateTime(reader.GetOrdinal("StartedAt")),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("CompletedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CompletedAt")),
            CancellationRequestedAt = reader.IsDBNull(reader.GetOrdinal("CancellationRequestedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("CancellationRequestedAt")),
            CancellationReason = reader.IsDBNull(reader.GetOrdinal("CancellationReason")) ? null : reader.GetString(reader.GetOrdinal("CancellationReason")),
            CreditBudget = reader.GetDecimal(reader.GetOrdinal("CreditBudget")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
