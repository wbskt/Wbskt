using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class TriggerRegistrationProvider : BaseSqlProvider, ITriggerRegistrationProvider
{
    private readonly string _connectionString;

    public TriggerRegistrationProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<TriggerRegistrationRow> InsertAsync(TriggerRegistrationRow row, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.TriggerRegistration_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowDefinitionId", row.WorkflowDefinitionId);
        command.Parameters.AddWithValue("@WorkflowRefId", row.WorkflowRefId);
        command.Parameters.AddWithValue("@WorkflowVersion", row.WorkflowVersion);
        command.Parameters.AddWithValue("@TriggerNodeId", row.TriggerNodeId);
        command.Parameters.AddWithValue("@TriggerKind", row.TriggerKind);
        command.Parameters.AddWithValue("@TriggerKey", row.TriggerKey);
        command.Parameters.AddWithValue("@CorrelationExpression", (object?)row.CorrelationExpression ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConcurrencyPolicy", row.ConcurrencyPolicy);
        command.Parameters.AddWithValue("@FilterExpression", (object?)row.FilterExpression ?? DBNull.Value);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("TriggerRegistration_Insert did not return a row.");
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetByTriggerKeyAsync(string triggerKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.TriggerRegistration_GetBy_TriggerKey", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@TriggerKey", triggerKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<TriggerRegistrationRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetActiveByChannelAsync(string channelKind, string channelKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            SELECT
                Id,
                WorkflowDefinitionId,
                WorkflowRefId,
                WorkflowVersion,
                TriggerNodeId,
                TriggerKind,
                TriggerKey,
                CorrelationExpression,
                ConcurrencyPolicy,
                FilterExpression,
                CreatedAt
            FROM dbo.TriggerRegistrations
            WHERE TriggerKind = @TriggerKind
              AND TriggerKey = @TriggerKey;
            """,
            connection);
        command.CommandType = CommandType.Text;

        command.Parameters.AddWithValue("@TriggerKind", channelKind);
        command.Parameters.AddWithValue("@TriggerKey", channelKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<TriggerRegistrationRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<TriggerRegistrationRow>> GetAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.TriggerRegistration_GetAllBy_WorkflowDefinitionId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<TriggerRegistrationRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.TriggerRegistration_DeleteAllBy_WorkflowDefinitionId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static TriggerRegistrationRow Map(DbDataReader reader)
    {
        return new TriggerRegistrationRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            WorkflowVersion = reader.GetInt32(reader.GetOrdinal("WorkflowVersion")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            TriggerKind = reader.GetString(reader.GetOrdinal("TriggerKind")),
            TriggerKey = reader.GetString(reader.GetOrdinal("TriggerKey")),
            CorrelationExpression = reader.IsDBNull(reader.GetOrdinal("CorrelationExpression")) ? null : reader.GetString(reader.GetOrdinal("CorrelationExpression")),
            ConcurrencyPolicy = reader.GetString(reader.GetOrdinal("ConcurrencyPolicy")),
            FilterExpression = reader.IsDBNull(reader.GetOrdinal("FilterExpression")) ? null : reader.GetString(reader.GetOrdinal("FilterExpression")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
