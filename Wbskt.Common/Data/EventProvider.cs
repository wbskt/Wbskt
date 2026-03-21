using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Common.Abstraction.Interfaces;
using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Common.Data;

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

    public async Task<IPagedList<EventLogResponse>> GetLogsAsync(int workspaceId, string? eventName, EventCriticality? criticality, int? policyId, int? clientId, int? workflowId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await ExecutePagedCollectionAsync(
            "dbo.EventLog_GetBy_Workspace",
            p =>
            {
                p.AddWithValue("@WorkspaceId", workspaceId);
                p.AddWithValue("@PolicyId", policyId);
                p.AddWithValue("@ClientId", clientId);
                p.AddWithValue("@WorkflowId", workflowId);
                p.AddWithValue("@EventName", (object?)eventName ?? DBNull.Value);
                p.AddWithValue("@Criticality", (object?)criticality ?? DBNull.Value);
                p.AddWithValue("@Skip", skip);
                p.AddWithValue("@Take", take);
                p.Add("@TotalCount", SqlDbType.Int).Direction = ParameterDirection.Output;
            },
            MapEventLog,
            cancellationToken);
    }

    private static EventLogResponse MapEventLog(SqlDataReader reader)
    {
        return new EventLogResponse(
            reader.GetString(reader.GetOrdinal("EventName")),
            reader.GetString(reader.GetOrdinal("EventData")),
            (EventCriticality)reader.GetByte(reader.GetOrdinal("EventCriticality")),
            reader.IsDBNull(reader.GetOrdinal("PolicyRefId")) ? null : reader.GetGuid(reader.GetOrdinal("PolicyRefId")),
            reader.IsDBNull(reader.GetOrdinal("ClientRefId")) ? null : reader.GetGuid(reader.GetOrdinal("ClientRefId")),
            reader.IsDBNull(reader.GetOrdinal("WorkflowRefId")) ? null : reader.GetGuid(reader.GetOrdinal("WorkflowRefId")),
            reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
        );
    }
}
