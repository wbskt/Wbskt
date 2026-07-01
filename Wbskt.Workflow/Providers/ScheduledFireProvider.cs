using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class ScheduledFireProvider : BaseSqlProvider, IScheduledFireProvider
{
    public ScheduledFireProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<ScheduledFireRow> InsertAsync(Guid triggerNodeId, int workflowDefinitionId, Guid workflowRefId, string cronOrInterval, DateTime nextFireAt, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.ScheduledFire_Insert",
            p =>
            {
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId);
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@CronOrInterval", cronOrInterval);
                p.AddWithValue("@NextFireAt", nextFireAt);
            },
            Map,
            new InvalidOperationException("ScheduledFire_Insert did not return a row."),
            ct
        );
    }

    public async Task<IReadOnlyCollection<ScheduledFireRow>> LeaseDueAsync(int leaseSec, int batch, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.ScheduledFire_LeaseDue",
            p =>
            {
                p.AddWithValue("@LeaseSec", leaseSec);
                p.AddWithValue("@Batch", batch);
            },
            Map,
            ct
        );
    }

    public async Task<ScheduledFireRow> AdvanceNextAsync(int id, DateTime nextFireAt, CancellationToken ct)
    {
        return await ExecuteSingleAsync(
            "dbo.ScheduledFire_AdvanceNext",
            p =>
            {
                p.AddWithValue("@Id", id);
                p.AddWithValue("@NextFireAt", nextFireAt);
            },
            Map,
            new InvalidOperationException("ScheduledFire_AdvanceNext did not return a row."),
            ct
        );
    }

    public async Task DeleteByIdAsync(long id, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ScheduledFire_DeleteById",
            p => p.AddWithValue("@Id", checked((int)id)),
            ct
        );
    }

    public async Task DeleteAllByWorkflowDefinitionIdAsync(int workflowDefinitionId, CancellationToken ct)
    {
        await ExecuteNonQueryAsync(
            "dbo.ScheduledFire_DeleteAllBy_WorkflowDefinitionId",
            p => p.AddWithValue("@WorkflowDefinitionId", workflowDefinitionId),
            ct
        );
    }

    internal static ScheduledFireRow Map(DbDataReader reader)
    {
        return new ScheduledFireRow
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            TriggerNodeId = reader.GetGuid(reader.GetOrdinal("TriggerNodeId")),
            WorkflowDefinitionId = reader.GetInt32(reader.GetOrdinal("WorkflowDefinitionId")),
            WorkflowRefId = reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            CronOrInterval = reader.GetString(reader.GetOrdinal("CronOrInterval")),
            NextFireAt = reader.GetDateTime(reader.GetOrdinal("NextFireAt")),
            LeasedUntil = reader.IsDBNull(reader.GetOrdinal("LeasedUntil")) ? null : reader.GetDateTime(reader.GetOrdinal("LeasedUntil")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        };
    }
}
