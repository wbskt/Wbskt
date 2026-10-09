using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class EventProvider : BaseSqlProvider, IEventProvider
{
    public EventProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> GetOrInsertEventIdAsync(string eventName, short criticality, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Events_GetOrInsert", p =>
        {
            p.AddWithValue("@EventName", eventName);
            p.AddWithValue("@EventCriticality", criticality);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (int)parameters["@Id"].Value;
    }

    public async Task InsertBatchAsync(DataTable logs, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.EventLogs_InsertBatch", p =>
        {
            var parameter = p.AddWithValue("@Logs", logs);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = "dbo.EventLogTableType";
        }, cancellationToken);
    }

    public async Task<int> DeleteBeforeAsync(DateTime cutoffUtc, int batchSize, IReadOnlyCollection<int>? eventIds = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.EventLogs_DeleteBefore", p =>
        {
            p.Add("@CutoffUtc", SqlDbType.DateTime2).Value = cutoffUtc;
            p.AddWithValue("@BatchSize", batchSize);
            p.Add("@EventIds", SqlDbType.NVarChar, -1).Value = eventIds is null ? DBNull.Value : string.Join(',', eventIds);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<EventLogRow>> GetLogsAsync(int workspaceId, EventLogFilter filter, long? cursorId, int take, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.EventLog_GetBy_Workspace",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@EventName", (object?)filter.EventName ?? DBNull.Value);
                p.Add("@EventNames", SqlDbType.NVarChar, -1).Value = Names(filter.EventNames);
                p.Add("@ExcludeEventNames", SqlDbType.NVarChar, -1).Value = Names(filter.ExcludeEventNames);
                p.AddWithValue("@Criticality", (object?)filter.Criticality ?? DBNull.Value);
                p.AddWithValue("@PolicyId", (object?)filter.PolicyId ?? DBNull.Value);
                p.AddWithValue("@ClientId", (object?)filter.ClientId ?? DBNull.Value);
                p.AddWithValue("@WorkflowRefId", (object?)filter.WorkflowRefId ?? DBNull.Value);
                p.AddWithValue("@UserRefId", (object?)filter.UserRefId ?? DBNull.Value);
                p.Add("@FromUtc", SqlDbType.DateTime2).Value = (object?)filter.FromUtc ?? DBNull.Value;
                p.Add("@ToUtc", SqlDbType.DateTime2).Value = (object?)filter.ToUtc ?? DBNull.Value;
                p.Add("@Search", SqlDbType.NVarChar, 200).Value = filter.Search is null ? DBNull.Value : EscapeLike(filter.Search);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
                p.AddWithValue("@Take", take);
            },
            MapEventLogRow,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, long>> CountByEventAsync(int workspaceId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await ExecuteCollectionAsync(
            "dbo.EventLog_CountBy_Workspace",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.Add("@FromUtc", SqlDbType.DateTime2).Value = fromUtc;
                p.Add("@ToUtc", SqlDbType.DateTime2).Value = toUtc;
            },
            reader => (Name: reader.GetString(reader.GetOrdinal("EventName")), Count: reader.GetInt64(reader.GetOrdinal("EntryCount"))),
            cancellationToken);

        return rows.ToDictionary(r => r.Name, r => r.Count, StringComparer.Ordinal);
    }

    private static object Names(IReadOnlyCollection<string>? names) => names is null ? DBNull.Value : string.Join(',', names);

    /// <summary>Makes the search text match itself literally in a LIKE with '\' as the escape character.</summary>
    internal static string EscapeLike(string text) => text
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_")
        .Replace("[", @"\[");

    public async Task<IReadOnlyCollection<EventLogRow>> GetClientCommsAsync(int workspaceId, int clientId, string? direction, long? cursorId, int take, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.EventLog_GetCommsBy_Client",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@Direction", (object?)direction ?? DBNull.Value);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
                p.AddWithValue("@Take", take);
            },
            MapEventLogRow,
            cancellationToken);
    }

    private static EventLogRow MapEventLogRow(SqlDataReader reader) =>
        new(reader.GetInt64(reader.GetOrdinal("Id")), MapEventLog(reader));

    private static EventLogResponse MapEventLog(SqlDataReader reader)
    {
        return new EventLogResponse(
            reader.GetString(reader.GetOrdinal("EventName")),
            reader.GetString(reader.GetOrdinal("EventData")),
            (EventCriticality)reader.GetByte(reader.GetOrdinal("EventCriticality")),
            reader.IsDBNull(reader.GetOrdinal("PolicyRefId")) ? null : reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            reader.IsDBNull(reader.GetOrdinal("ClientRefId")) ? null : reader.GetGuid(reader.GetOrdinal("ClientRefId")),
            reader.IsDBNull(reader.GetOrdinal("WorkflowRefId")) ? null : reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            reader.IsDBNull(reader.GetOrdinal("UserRefId")) ? null : reader.GetGuid(reader.GetOrdinal("UserRefId")),
            reader.GetInt64(reader.GetOrdinal("Id"))
        );
    }
}
