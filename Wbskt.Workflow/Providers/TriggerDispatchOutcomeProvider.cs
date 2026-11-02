using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class TriggerDispatchOutcomeProvider : BaseSqlProvider, ITriggerDispatchOutcomeProvider
{
    public TriggerDispatchOutcomeProvider(IConfiguration configuration) : base(configuration) { }

    public async Task RecordManyAsync(IReadOnlyList<TriggerDispatchOutcomeRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using var table = new DataTable();
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("WorkflowRefId", typeof(Guid));
        table.Columns.Add("TriggerNodeId", typeof(Guid));
        table.Columns.Add("OccurredAt", typeof(DateTime));
        table.Columns.Add("ChannelKind", typeof(string));
        table.Columns.Add("MessageType", typeof(string));
        table.Columns.Add("Outcome", typeof(string));
        table.Columns.Add("CorrelationKey", typeof(string));
        table.Columns.Add("RunRefId", typeof(Guid));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            table.Rows.Add(i, row.WorkflowRefId, row.TriggerNodeId, row.OccurredAt, Truncate(row.ChannelKind, 64),
                (object?)Truncate(row.MessageType, 200) ?? DBNull.Value, row.Outcome,
                (object?)Truncate(row.CorrelationKey, 400) ?? DBNull.Value, (object?)row.RunRefId ?? DBNull.Value);
        }

        await ExecuteNonQueryAsync(
            "dbo.TriggerDispatchOutcome_RecordMany",
            p =>
            {
                var parameter = p.AddWithValue("@Outcomes", table);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = "dbo.TriggerDispatchOutcomeTableType";
                p.AddWithValue("@Keep", ITriggerDispatchOutcomeProvider.Keep);
            },
            ct
        );
    }

    public async Task<IReadOnlyCollection<TriggerDispatchOutcomeRow>> GetRecentAsync(Guid workflowRefId, Guid triggerNodeId, int take, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.TriggerDispatchOutcome_GetRecent",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
                p.AddWithValue("@Take", take);
            },
            reader => new TriggerDispatchOutcomeRow
            {
                WorkflowRefId = workflowRefId,
                TriggerNodeId = triggerNodeId,
                OccurredAt = reader.GetDateTime(reader.GetOrdinal("OccurredAt")),
                ChannelKind = reader.GetString(reader.GetOrdinal("ChannelKind")),
                MessageType = NullableString(reader, "MessageType"),
                Outcome = reader.GetString(reader.GetOrdinal("Outcome")),
                CorrelationKey = NullableString(reader, "CorrelationKey"),
                RunRefId = reader.IsDBNull(reader.GetOrdinal("RunRefId")) ? null : reader.GetGuid(reader.GetOrdinal("RunRefId"))
            },
            ct
        );
    }

    public async Task<TriggerHoldStateRow?> FindHoldStateAsync(Guid workflowRefId, Guid triggerNodeId, CancellationToken ct)
    {
        return await ExecuteFindAsync(
            "dbo.ClientHoldState_GetBy_Trigger",
            p =>
            {
                p.AddWithValue("@WorkflowRefId", workflowRefId);
                p.AddWithValue("@TriggerNodeId", triggerNodeId);
            },
            reader => new TriggerHoldStateRow
            {
                State = reader.GetString(reader.GetOrdinal("State")),
                SinceAt = NullableDateTime(reader, "SinceAt"),
                DueAt = NullableDateTime(reader, "DueAt"),
                LastEventAt = reader.GetDateTime(reader.GetOrdinal("LastEventAt"))
            },
            ct
        );
    }

    // The columns are bounded; a longer value (a device's free-form message type) is cut rather than
    // failing the record.
    private static string? Truncate(string? value, int max) => value is { Length: var length } && length > max ? value[..max] : value;

    private static string? NullableString(DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? NullableDateTime(DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
