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
            LocalJson = """{"trigger":{}}""", LastOutputJson = null,
            CreatedAt = now, UpdatedAt = now, RowVersion = Array.Empty<byte>()
        };

        RunStartRow started = await ProviderFactory.Run(connectionString).StartAsync(run, branch, null, CancellationToken.None);
        return started.Run;
    }
}
