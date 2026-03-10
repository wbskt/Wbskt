using System.Data;
using Microsoft.Extensions.Configuration;
using Wbskt.Common.Abstraction.Interfaces;

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
}
