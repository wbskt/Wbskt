using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class WorkflowsDatabaseReader : IWorkflowsDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowsDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<WorkflowRecord>> GetAllForUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_GetAll_ByUserId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@UserId", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var workflows = new List<WorkflowRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            workflows.Add(new WorkflowRecord
            {
                Id = reader.GetInt32(0),
                RefId = reader.GetGuid(1),
                UserId = reader.GetInt32(2),
                Name = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsEnabled = reader.GetBoolean(5),
                TriggerType = reader.GetString(6),
                TriggerConfiguration = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastModified = reader.GetDateTime(8)
            });
        }

        return workflows;
    }

    public async Task<WorkflowRecord?> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_GetBy_RefId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@RefId", refId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new WorkflowRecord
            {
                Id = reader.GetInt32(0),
                RefId = reader.GetGuid(1),
                UserId = reader.GetInt32(2),
                Name = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsEnabled = reader.GetBoolean(5),
                TriggerType = reader.GetString(6),
                TriggerConfiguration = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastModified = reader.GetDateTime(8)
            };
        }

        return null;
    }

    public async Task<List<WorkflowRecord>> GetActiveTimedWorkflowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_GetActive_ByTriggerType";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@TriggerType", "Timed");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var workflows = new List<WorkflowRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            workflows.Add(new WorkflowRecord
            {
                Id = reader.GetInt32(0),
                RefId = reader.GetGuid(1),
                UserId = reader.GetInt32(2),
                Name = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsEnabled = reader.GetBoolean(5),
                TriggerType = reader.GetString(6),
                TriggerConfiguration = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastModified = reader.GetDateTime(8)
            });
        }

        return workflows;
    }
}
