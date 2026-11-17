using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Integrations;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Providers;

internal sealed class WorkflowStateProvider : BaseSqlProvider, IWorkflowStateProvider
{
    public WorkflowStateProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<WorkflowStateRow?> FindAsync(Guid refId, int workspaceId, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.Workflow_FindBy_RefId",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
            },
            Map,
            ct
        );
    }

    public async Task<WorkflowEnableResult> EnableAsync(Guid refId, int workspaceId, int version, int changedBy, TriggerPlan plan, IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs, CancellationToken ct)
    {
        SqlParameterCollection parameters = await ExecuteNonQueryAsync(
            "dbo.Workflow_Enable",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@Version", version);
                p.AddWithValue("@ChangedBy", changedBy);
                AddTable(p, "@Registrations", "dbo.TriggerPlanRegistrationTableType", RegistrationTable(plan.Registrations));
                AddTable(p, "@Schedules", "dbo.TriggerPlanScheduleTableType", ScheduleTable(plan.Schedules));
                AddTable(p, "@IntegrationRefs", "dbo.IntegrationRefTableType", IntegrationTable(integrationRefs));
                AddOutput(p, "@Changed", SqlDbType.Bit);
                AddOutput(p, "@PreviousLiveDefinitionId", SqlDbType.Int);
                AddOutput(p, "@LiveDefinitionId", SqlDbType.Int);
                AddOutput(p, "@DroppedQueuedEvents", SqlDbType.Int);
            },
            ct
        );

        return new WorkflowEnableResult(
            (bool)parameters["@Changed"].Value,
            NullableInt(parameters["@PreviousLiveDefinitionId"].Value),
            (int)parameters["@LiveDefinitionId"].Value,
            (int)parameters["@DroppedQueuedEvents"].Value);
    }

    public async Task<WorkflowDisableResult> DisableAsync(Guid refId, int workspaceId, int changedBy, bool deprecate, CancellationToken ct)
    {
        // The output parameters are filled once the reader closes, which ExecuteCollectionAsync does
        // before it returns, so they are kept here to be read after.
        SqlParameterCollection? parameters = null;
        IReadOnlyCollection<long> activeRunIds = await ExecuteCollectionAsync(
            "dbo.Workflow_Disable",
            p =>
            {
                parameters = p;
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@ChangedBy", changedBy);
                p.AddWithValue("@Deprecate", deprecate);
                AddOutput(p, "@Changed", SqlDbType.Bit);
                AddOutput(p, "@PreviousLiveDefinitionId", SqlDbType.Int);
                AddOutput(p, "@DroppedQueuedEvents", SqlDbType.Int);
            },
            reader => (long)reader.GetInt32(reader.GetOrdinal("Id")),
            ct
        );

        return new WorkflowDisableResult(
            (bool)parameters!["@Changed"].Value,
            NullableInt(parameters["@PreviousLiveDefinitionId"].Value),
            (int)parameters["@DroppedQueuedEvents"].Value,
            activeRunIds);
    }

    public async Task ReinstateAsync(Guid refId, int workspaceId, int changedBy, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.Workflow_Reinstate",
            p =>
            {
                p.AddWithValue("@RefId", refId);
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@ChangedBy", changedBy);
            },
            ct
        );
    }

    private static void AddTable(SqlParameterCollection p, string name, string typeName, DataTable table)
    {
        SqlParameter parameter = p.AddWithValue(name, table);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = typeName;
    }

    private static void AddOutput(SqlParameterCollection p, string name, SqlDbType type)
    {
        p.Add(name, type).Direction = ParameterDirection.Output;
    }

    private static int? NullableInt(object value) => value is DBNull or null ? null : (int)value;

    private static DataTable RegistrationTable(IReadOnlyCollection<PlannedTrigger> registrations)
    {
        var table = new DataTable();
        table.Columns.Add("TriggerNodeId", typeof(Guid));
        table.Columns.Add("TriggerKind", typeof(string));
        table.Columns.Add("TriggerKey", typeof(string));
        table.Columns.Add("CorrelationExpression", typeof(string));
        table.Columns.Add("ConcurrencyPolicy", typeof(string));
        table.Columns.Add("FilterExpression", typeof(string));
        table.Columns.Add("WebhookSecret", typeof(string));
        foreach (PlannedTrigger r in registrations)
        {
            table.Rows.Add(r.TriggerNodeId, r.TriggerKind, r.TriggerKey, (object?)r.CorrelationExpression ?? DBNull.Value, r.ConcurrencyPolicy, (object?)r.FilterExpression ?? DBNull.Value, (object?)r.WebhookSecret ?? DBNull.Value);
        }

        return table;
    }

    private static DataTable ScheduleTable(IReadOnlyCollection<PlannedSchedule> schedules)
    {
        var table = new DataTable();
        table.Columns.Add("TriggerNodeId", typeof(Guid));
        table.Columns.Add("CronOrInterval", typeof(string));
        table.Columns.Add("NextFireAt", typeof(DateTime));
        foreach (PlannedSchedule s in schedules)
        {
            table.Rows.Add(s.TriggerNodeId, s.CronOrInterval, s.NextFireAt);
        }

        return table;
    }

    private static DataTable IntegrationTable(IReadOnlyCollection<WorkflowIntegrationRef> integrationRefs)
    {
        var table = new DataTable();
        table.Columns.Add("NodeId", typeof(Guid));
        table.Columns.Add("NodeName", typeof(string));
        table.Columns.Add("IntegrationRefId", typeof(Guid));
        foreach (WorkflowIntegrationRef r in integrationRefs)
        {
            table.Rows.Add(r.NodeId, r.NodeName.Length <= WorkflowIntegrationRef.MaxNodeNameLength ? r.NodeName : r.NodeName[..WorkflowIntegrationRef.MaxNodeNameLength], r.IntegrationRefId);
        }

        return table;
    }

    private static WorkflowStateRow Map(DbDataReader reader)
    {
        int liveDefinitionId = reader.GetOrdinal("LiveDefinitionId");
        int liveVersion = reader.GetOrdinal("LiveVersion");
        return new WorkflowStateRow(
            reader.GetGuid(reader.GetOrdinal("RefId")),
            reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            (WorkflowState)reader.GetByte(reader.GetOrdinal("State")),
            reader.IsDBNull(liveDefinitionId) ? null : reader.GetInt32(liveDefinitionId),
            reader.IsDBNull(liveVersion) ? null : reader.GetInt32(liveVersion),
            reader.GetInt32(reader.GetOrdinal("LatestVersion")));
    }
}
