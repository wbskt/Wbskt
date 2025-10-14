using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class WorkflowStepExecutionsDatabaseReader : IWorkflowStepExecutionsReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowStepExecutionsDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<WorkflowStepExecutionRecord>> GetAllForExecutionAsync(int executionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowStepExecutions_GetAll_ByExecutionId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@WorkflowExecutionId", executionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var executions = new List<WorkflowStepExecutionRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(new WorkflowStepExecutionRecord
            {
                Id = reader.GetInt32(0),
                WorkflowExecutionId = reader.GetInt32(1),
                WorkflowStepId = reader.GetInt32(2),
                Status = reader.GetString(3),
                StartedAt = reader.GetDateTime(4),
                CompletedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                InputContext = reader.IsDBNull(6) ? null : reader.GetString(6),
                OutputContext = reader.IsDBNull(7) ? null : reader.GetString(7),
                ErrorLog = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return executions;
    }
}
