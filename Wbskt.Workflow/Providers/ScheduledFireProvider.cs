using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class ScheduledFireProvider : BaseSqlProvider, IScheduledFireProvider
{
    private readonly string _connectionString;

    public ScheduledFireProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.ScheduledFire_Insert", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
        command.Parameters.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);
        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        command.Parameters.AddWithValue("@CronOrInterval", cronOrInterval);
        command.Parameters.AddWithValue("@NextFireAt", nextFireAt);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("ScheduledFire_Insert did not return a row.");
    }

    public async Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.ScheduledFire_LeaseDue", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@LeaseSec", leaseSec);
        command.Parameters.AddWithValue("@Batch", batch);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<ScheduledFireRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
    }

    public async Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.ScheduledFire_AdvanceNext", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@NextFireAt", nextFireAt);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            return Map(reader);
        }

        throw new InvalidOperationException("ScheduledFire_AdvanceNext did not return a row.");
    }

    public async Task DeleteByIdAsync(long id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand(
            """
            DELETE FROM dbo.ScheduledFires
            WHERE Id = @Id;
            """,
            connection);
        command.CommandType = CommandType.Text;
        command.Parameters.AddWithValue("@Id", checked((int)id));

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.ScheduledFire_DeleteAllBy_WorkflowDefinitionId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static ScheduledFireRow Map(DbDataReader reader)
    {
        return new ScheduledFireRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            CronOrInterval = reader.GetString(reader.GetOrdinal("CronOrInterval")),
            NextFireAt = reader.GetDateTime(reader.GetOrdinal("NextFireAt")),
            LeasedUntil = reader.IsDBNull(reader.GetOrdinal("LeasedUntil")) ? null : reader.GetDateTime(reader.GetOrdinal("LeasedUntil")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
