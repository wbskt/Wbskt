using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

internal sealed class HistoryEventProvider : BaseSqlProvider, IHistoryEventProvider
{
    public HistoryEventProvider(IConfiguration configuration) : base(configuration) { }

    public async Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
    {
        var eventsTable = new DataTable();
        eventsTable.Columns.Add("RunId", typeof(int));
        eventsTable.Columns.Add("BranchRefId", typeof(Guid));
        eventsTable.Columns.Add("NodeId", typeof(Guid));
        eventsTable.Columns.Add("EventKind", typeof(string));
        eventsTable.Columns.Add("Severity", typeof(string));
        eventsTable.Columns.Add("PayloadJson", typeof(string));
        eventsTable.Columns.Add("Timestamp", typeof(DateTime));

        foreach (var evt in events)
        {
            eventsTable.Rows.Add(
                evt.RunId,
                (object?)evt.BranchRefId ?? DBNull.Value,
                (object?)evt.NodeId ?? DBNull.Value,
                evt.EventKind,
                evt.Severity,
                (object?)evt.PayloadJson ?? DBNull.Value,
                evt.Timestamp
            );
        }

        await ExecuteNonQueryAsync(
            "dbo.HistoryEvent_InsertBatch",
            p =>
            {
                var parameter = p.AddWithValue("@Events", eventsTable);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = "dbo.HistoryEventTableType";
            },
            ct
        );
    }

    public async Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct)
    {
        return await ExecuteCollectionAsync(
            "dbo.HistoryEvent_GetBy_RunId",
            p =>
            {
                p.AddWithValue("@RunId", runId);
                p.AddWithValue("@AfterEventId", afterEventId);
                p.AddWithValue("@PageSize", pageSize);
            },
            Map,
            ct
        );
    }

    public async Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct)
    {
        var result = await ExecuteScalarAsync<object>(
            "dbo.HistoryEvent_DeleteForRetiredRuns",
            p =>
            {
                p.AddWithValue("@CutoffUtc", cutoffUtc);
                p.AddWithValue("@BatchSize", batchSize);
                p.AddWithValue("@ElevatedCutoffUtc", (object?)elevatedCutoffUtc ?? DBNull.Value);
            },
            ct
        );
        return result is int count ? count : Convert.ToInt32(result ?? 0);
    }

    internal static HistoryEventRow Map(DbDataReader reader)
    {
        return new HistoryEventRow
        {
            HistoryEventId = reader.GetInt64(reader.GetOrdinal("HistoryEventId")),
            RunId = reader.GetInt32(reader.GetOrdinal("RunId")),
            BranchRefId = reader.IsDBNull(reader.GetOrdinal("BranchRefId")) ? null : reader.GetGuid(reader.GetOrdinal("BranchRefId")),
            NodeId = reader.IsDBNull(reader.GetOrdinal("NodeId")) ? null : reader.GetGuid(reader.GetOrdinal("NodeId")),
            EventKind = reader.GetString(reader.GetOrdinal("EventKind")),
            Severity = reader.GetString(reader.GetOrdinal("Severity")),
            PayloadJson = reader.IsDBNull(reader.GetOrdinal("PayloadJson")) ? null : reader.GetString(reader.GetOrdinal("PayloadJson")),
            Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp"))
        };
    }
}
