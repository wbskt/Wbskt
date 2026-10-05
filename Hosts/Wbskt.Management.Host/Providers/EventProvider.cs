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

    public async Task<IReadOnlyCollection<EventLogRow>> GetLogsAsync(int workspaceId, string? eventName, EventCriticality? criticality, int? policyId, int? clientId, int? workflowId, long? cursorId, int take, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.EventLog_GetBy_Workspace",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@PolicyId", (object?)policyId ?? DBNull.Value);
                p.AddWithValue("@ClientId", (object?)clientId ?? DBNull.Value);
                p.AddWithValue("@WorkflowId", (object?)workflowId ?? DBNull.Value);
                p.AddWithValue("@EventName", (object?)eventName ?? DBNull.Value);
                p.AddWithValue("@Criticality", (object?)criticality ?? DBNull.Value);
                p.AddWithValue("@CursorId", (object?)cursorId ?? DBNull.Value);
                p.AddWithValue("@Take", take);
            },
            MapEventLogRow,
            cancellationToken);
    }

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
            reader.IsDBNull(reader.GetOrdinal("UserRefId")) ? null : reader.GetGuid(reader.GetOrdinal("UserRefId"))
        );
    }
}
