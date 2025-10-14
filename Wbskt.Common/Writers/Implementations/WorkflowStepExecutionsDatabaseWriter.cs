using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class WorkflowStepExecutionsDatabaseWriter : IWorkflowStepExecutionsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowStepExecutionsDatabaseWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<int> CreateAsync(WorkflowStepExecutionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowStepExecutions_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowExecutionId", record.WorkflowExecutionId);
        command.Parameters.AddWithValue("@WorkflowStepId", record.WorkflowStepId);
        command.Parameters.AddWithValue("@Status", record.Status);
        command.Parameters.AddWithValue("@StartedAt", record.StartedAt);
        command.Parameters.AddWithValue("@InputContext", (object?)record.InputContext ?? DBNull.Value);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)idParameter.Value;
    }

    public async Task UpdateAsync(WorkflowStepExecutionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowStepExecutions_Update";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", record.Id);
        command.Parameters.AddWithValue("@Status", record.Status);
        command.Parameters.AddWithValue("@CompletedAt", (object?)record.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@OutputContext", (object?)record.OutputContext ?? DBNull.Value);
        command.Parameters.AddWithValue("@ErrorLog", (object?)record.ErrorLog ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
