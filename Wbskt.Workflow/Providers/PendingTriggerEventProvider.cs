using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class PendingTriggerEventProvider : BaseSqlProvider, IPendingTriggerEventProvider
{
    private readonly string _connectionString;

    public PendingTriggerEventProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<PendingTriggerEventRow> EnqueueAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, string inboundEventJson, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.PendingTriggerEvent_Enqueue", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
        command.Parameters.AddWithValue("@CorrelationKey", correlationKey);
        command.Parameters.AddWithValue("@InboundEventJson", inboundEventJson);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("PendingTriggerEvent_Enqueue did not return a row.");
    }

    public async Task<PendingTriggerEventRow?> DequeueNextAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.PendingTriggerEvent_DequeueNext", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
        command.Parameters.AddWithValue("@CorrelationKey", correlationKey);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        return null;
    }

    public async Task DeleteAllByRunKeyAsync(Guid workflowRefId, Guid triggerNodeId, string correlationKey, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.PendingTriggerEvent_DeleteAllBy_RunKey", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
        command.Parameters.AddWithValue("@CorrelationKey", correlationKey);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
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
