using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

/// <summary>
/// Makes a published version live the way the API does, through <c>dbo.Workflow_Enable</c>, so a test
/// that starts runs or matches triggers sees the same registrations, schedules and state as production.
/// </summary>
internal static class LiveWorkflows
{
    public static Task EnableAsync(string connectionString, Guid refId, int workspaceId, int version = 1, TriggerPlan? plan = null) =>
        ProviderFactory.WorkflowState(connectionString).EnableAsync(refId, workspaceId, version, 1, plan ?? TriggerPlan.Empty, [], CancellationToken.None);

    /// <summary>Makes the version just published live, for a test that starts runs of it.</summary>
    public static async Task<WorkflowDefinitionRow> LiveAsync(this Task<WorkflowDefinitionRow> published, string connectionString)
    {
        WorkflowDefinitionRow row = await published;
        await EnableAsync(connectionString, row.RefId, row.WorkspaceId, row.Version);
        return row;
    }

    /// <summary>A plan of one registration, for a test that matches a single trigger by its key.</summary>
    public static TriggerPlan Registration(Guid triggerNodeId, string triggerKind, string triggerKey, string concurrencyPolicy = "Queue", string? filterExpression = null) =>
        new([new PlannedTrigger(triggerNodeId, triggerKind, triggerKey, null, concurrencyPolicy, filterExpression, null, null)], []);
}
