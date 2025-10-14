using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class WorkflowExecutionsDatabaseReader : IWorkflowExecutionsReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowExecutionsDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<WorkflowExecutionRecord>> GetAllForWorkflowAsync(Guid workflowRefId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowExecutions_GetAll_ByWorkflowRefId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var executions = new List<WorkflowExecutionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(new WorkflowExecutionRecord
            {
                Id = reader.GetInt32(0),
                WorkflowRefId = reader.GetGuid(1),
                Status = reader.GetString(2),
                TriggeredAt = reader.GetDateTime(3),
                CompletedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                InitialContext = reader.IsDBNull(5) ? null : reader.GetString(5),
                ErrorLog = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return executions;
    }

    public async Task<WorkflowExecutionRecord?> GetByIdAsync(int executionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowExecutions_GetBy_Id";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@Id", executionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            return new WorkflowExecutionRecord
            {
                Id = reader.GetInt32(0),
                WorkflowRefId = reader.GetGuid(1),
                Status = reader.GetString(2),
                TriggeredAt = reader.GetDateTime(3),
                CompletedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                InitialContext = reader.IsDBNull(5) ? null : reader.GetString(5),
                ErrorLog = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }

        return null;
    }
}
