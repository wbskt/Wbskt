using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Records;

namespace Wbskt.Common.Readers.Database.Implementation;

internal sealed class WorkflowStepsDatabaseReader : IWorkflowStepsDatabaseReader
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowStepsDatabaseReader(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<List<WorkflowStepRecord>> GetAllForWorkflowAsync(int workflowId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowSteps_GetBy_WorkflowId";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.AddWithValue("@WorkflowId", workflowId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var steps = new List<WorkflowStepRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            steps.Add(new WorkflowStepRecord
            {
                Id = reader.GetInt32(0),
                WorkflowId = reader.GetInt32(1),
                StepOrder = reader.GetInt32(2),
                Name = reader.GetString(3),
                StepType = reader.GetString(4),
                StepIdentifier = reader.GetString(5),
                StepConfiguration = reader.IsDBNull(6) ? null : reader.GetString(6),
                OnSuccessStepId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                OnFailureStepId = reader.IsDBNull(8) ? null : reader.GetInt32(8)
            });
        }

        return steps;
    }
}
