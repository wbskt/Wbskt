using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Infrastructure;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Providers;

public class HistoryEventProvider : BaseSqlProvider, IHistoryEventProvider
{
    private readonly string _connectionString;

    public HistoryEventProvider(IConfiguration configuration) : base(configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string not found.");
    }

    public async Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.HistoryEvent_InsertBatch", connection);
        command.CommandType = CommandType.StoredProcedure;

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

        var parameter = command.Parameters.AddWithValue("@Events", eventsTable);
        parameter.SqlDbType = SqlDbType.Structured;
        parameter.TypeName = "dbo.HistoryEventTableType";

        await connection.OpenAsync(ct);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("dbo.HistoryEvent_GetBy_RunId", connection);
        command.CommandType = CommandType.StoredProcedure;

        command.Parameters.AddWithValue("@RunId", runId);
        command.Parameters.AddWithValue("@AfterEventId", afterEventId);
        command.Parameters.AddWithValue("@PageSize", pageSize);

        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var results = new List<HistoryEventRow>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results.AsReadOnly();
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
