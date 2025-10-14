using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Common.Events;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Readers.Database.Implementation;
using Wbskt.Common.Records;
using Wbskt.EventBus;

namespace Wbskt.Common.Writers.Implementations;

internal sealed class WorkflowsWriter : IWorkflowsWriter
{
    private readonly IConnectionStringProvider _connectionStringProvider;
    private readonly IEventBus _eventBus;

    public WorkflowsWriter(IConnectionStringProvider connectionStringProvider, IEventBus eventBus)
    {
        _connectionStringProvider = connectionStringProvider;
        _eventBus = eventBus;
    }

    public async Task<int> CreateAsync(WorkflowRecord workflow, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_Insert";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", workflow.RefId);
        command.Parameters.AddWithValue("@UserId", workflow.UserId);
        command.Parameters.AddWithValue("@Name", workflow.Name);
        command.Parameters.AddWithValue("@Description", (object?)workflow.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsEnabled", workflow.IsEnabled);
        command.Parameters.AddWithValue("@TriggerType", workflow.TriggerType);
        command.Parameters.AddWithValue("@TriggerConfiguration", (object?)workflow.TriggerConfiguration ?? DBNull.Value);

        var idParameter = command.Parameters.Add("@Id", SqlDbType.Int);
        idParameter.Direction = ParameterDirection.Output;

        await command.ExecuteNonQueryAsync(cancellationToken);

        var newWorkflowId = (int)idParameter.Value;

        await _eventBus.PublishAsync(new WorkflowCreatedEvent { WorkflowRefId = workflow.RefId, UserId = workflow.UserId }, cancellationToken);

        return newWorkflowId;
    }

    public async Task UpdateAsync(WorkflowRecord workflow, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_Update";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", workflow.RefId);
        command.Parameters.AddWithValue("@Name", workflow.Name);
        command.Parameters.AddWithValue("@Description", (object?)workflow.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@IsEnabled", workflow.IsEnabled);
        command.Parameters.AddWithValue("@TriggerType", workflow.TriggerType);
        command.Parameters.AddWithValue("@TriggerConfiguration", (object?)workflow.TriggerConfiguration ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await _eventBus.PublishAsync(new WorkflowUpdatedEvent { WorkflowRefId = workflow.RefId, UserId = workflow.UserId }, cancellationToken);
    }

    public async Task DeleteAsync(Guid refId, CancellationToken cancellationToken)
    {
        // We need to get the user id before deleting the workflow to invalidate the cache
        // This is not ideal, but it's the best we can do for now.
        // In the future, we can implement a more granular cache for individual workflows.
        var workflow = await new WorkflowsDatabaseReader(_connectionStringProvider).GetByRefIdAsync(refId, cancellationToken);

        await using var connection = new SqlConnection(_connectionStringProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.Workflows_Delete_ByRefId";
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RefId", refId);

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (workflow != null)
        {
            await _eventBus.PublishAsync(new WorkflowDeletedEvent { WorkflowRefId = refId, UserId = workflow.UserId }, cancellationToken);
        }
    }
}
