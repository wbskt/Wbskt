using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Events;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.EventBus;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class WorkflowStepsWriter : IWorkflowStepsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IEventBus _eventBus;

    public WorkflowStepsWriter(IConnectionStringProvider connectionStringProvider, IEventBus eventBus)
    {
        _connectionStringProvider = connectionStringProvider;
        _eventBus = eventBus;
    }

    public async Task BulkUpdateForWorkflowAsync(int workflowId, IEnumerable<WorkflowStepRecord> steps, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var table = new DataTable();
        table.Columns.Add("StepOrder", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("StepType", typeof(string));
        table.Columns.Add("StepIdentifier", typeof(string));
        table.Columns.Add("StepConfiguration", typeof(string));
        table.Columns.Add("OnSuccessStepId", typeof(int));
        table.Columns.Add("OnFailureStepId", typeof(int));

        foreach (var step in steps)
        {
            table.Rows.Add(step.StepOrder, step.Name, step.StepType, step.StepIdentifier, step.StepConfiguration, step.OnSuccessStepId, step.OnFailureStepId);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.WorkflowSteps_BulkUpdate";
        command.CommandType = CommandType.StoredProcedure;

        var parameter = command.Parameters.AddWithValue("@WorkflowId", workflowId);
        parameter = command.Parameters.AddWithValue("@Steps", table);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.WorkflowStepType";

        await command.ExecuteNonQueryAsync(cancellationToken);

        await _eventBus.PublishAsync(new WorkflowStepsChangedEvent { WorkflowId = workflowId }, cancellationToken);
    }
}
