using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class WorkflowExecutionsDatabaseWriter : IWorkflowExecutionsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public WorkflowExecutionsDatabaseWriter(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<int> CreateAsync(WorkflowExecutionRecord execution, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowExecutions_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@WorkflowRefId", execution.WorkflowRefId);
        command.Parameters.AddWithValue("@Status", execution.Status);
        command.Parameters.AddWithValue("@TriggeredAt", execution.TriggeredAt);
        command.Parameters.AddWithValue("@InitialContext", (object?)execution.InitialContext ?? DBNull.Value);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        return (int)idParameter.Value;
    }

    public async Task UpdateAsync(WorkflowExecutionRecord execution, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowExecutions_Update";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@Id", execution.Id);
        command.Parameters.AddWithValue("@Status", execution.Status);
        command.Parameters.AddWithValue("@CompletedAt", (object?)execution.CompletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@ErrorLog", (object?)execution.ErrorLog ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
