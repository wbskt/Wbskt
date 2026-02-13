using System.Data;
using Microsoft.Extensions.Configuration;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Common.Data;

internal sealed class EventProvider : BaseSqlProvider, IEventProvider
{
    public EventProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<int> GetOrInsertEventIdAsync(string eventName, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.Events_GetOrInsert", p =>
        {
            p.AddWithValue("@EventName", eventName);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        return (int)parameters["@Id"].Value;
    }

    public async Task InsertEventLogAsync(int eventId, string eventData, DateTime createdAtUtc, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.EventLogs_Insert", p =>
        {
            p.AddWithValue("@EventId", eventId);
            p.AddWithValue("@EventData", eventData);
            p.AddWithValue("@CreatedAtUtc", createdAtUtc);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);
    }
}
