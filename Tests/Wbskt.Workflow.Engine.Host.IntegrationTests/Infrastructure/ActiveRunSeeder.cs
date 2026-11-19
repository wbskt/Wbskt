using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Starts a Running run that holds a workflow, trigger node and correlation key, which is what the Queue
/// policy's enqueue requires before it will queue anything behind it (#470).
/// </summary>
internal static class ActiveRunSeeder
{
    public static async Task<RunRow> StartAsync(string connectionString, int workflowDefinitionId, Guid workflowRefId, Guid triggerNodeId, string correlationKey)
    {
        DateTime now = DateTime.UtcNow;
        var run = new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = workflowDefinitionId, WorkflowRefId = workflowRefId, WorkflowVersion = 0,
            TriggerNodeId = triggerNodeId,
            CorrelationKey = correlationKey,
            Status = RunStatus.Running,
            StartedAt = now, CompletedAt = null, CancellationRequestedAt = null, CancellationReason = null,
            CreditBudget = 10m, CreatedAt = now
        };
        var branch = new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = 0,
            ParentBranchId = null, ForkCohortId = null,
            NodeId = triggerNodeId, Status = BranchStatus.Active, PendingTakePort = null,
            LocalJson = """{"trigger":{}}""",
            CreatedAt = now, UpdatedAt = now, RowVersion = Array.Empty<byte>()
        };

        RunStartRow started = await ProviderFactory.Run(connectionString).StartAsync(run, branch, null, CancellationToken.None);
        return started.Run;
    }

    /// <summary>
    /// Starts <paramref name="run"/> the way the engine does, through Run_Start with no plan limits, with
    /// a root branch Active on its trigger node. The test's way to make a run to hang rows off.
    /// </summary>
    public static async Task<RunStartRow> StartAsync(string connectionString, RunRow run)
    {
        DateTime now = DateTime.UtcNow;
        var root = new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = 0,
            ParentBranchId = null, ForkCohortId = null,
            NodeId = run.TriggerNodeId, Status = BranchStatus.Active, PendingTakePort = null,
            LocalJson = """{"trigger":{}}""",
            CreatedAt = now, UpdatedAt = now, RowVersion = Array.Empty<byte>()
        };

        return await ProviderFactory.Run(connectionString).StartAsync(run, root, null, CancellationToken.None);
    }

    /// <summary>
    /// Starts <paramref name="run"/> as <see cref="StartAsync(string, RunRow)"/> does, then ends its root
    /// branch: the branch is Completed and the active-branch count is back to zero, so the run holds no
    /// live work until the test adds some.
    /// </summary>
    public static async Task<RunRow> StartWithNoLiveWorkAsync(string connectionString, RunRow run)
    {
        RunStartRow started = await StartAsync(connectionString, run);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            UPDATE dbo.Branches SET Status = @Completed WHERE Id = @BranchId;
            UPDATE dbo.RunCounters SET ActiveBranchCount = 0 WHERE RunId = @RunId;
            """, connection);
        command.Parameters.AddWithValue("@Completed", BranchStatus.Completed);
        command.Parameters.AddWithValue("@BranchId", started.RootBranchId);
        command.Parameters.AddWithValue("@RunId", started.Run.Id);
        await command.ExecuteNonQueryAsync();

        return started.Run;
    }
}
