using System.Data;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class ClientMessageTypeProvider : BaseSqlProvider, IClientMessageTypeProvider
{
    public ClientMessageTypeProvider(IConfiguration configuration) : base(configuration) { }

    public async Task RecordAsync(IReadOnlyCollection<ClientMessageTypeTally> tallies, int maxPerClient, CancellationToken cancellationToken = default)
    {
        using var table = new DataTable();
        table.Columns.Add("ClientId", typeof(int));
        table.Columns.Add("Type", typeof(string));
        table.Columns.Add("MessageCount", typeof(int));
        table.Columns.Add("LastSeenAt", typeof(DateTime));
        table.Columns.Add("SamplePayload", typeof(string));
        foreach (var tally in tallies)
        {
            table.Rows.Add(tally.ClientId, tally.Type, tally.Count, tally.LastSeenAt, (object?)tally.SamplePayload ?? DBNull.Value);
        }

        await ExecuteNonQueryAsync("dbo.ClientMessageTypes_Record", p =>
        {
            var parameter = p.AddWithValue("@Rows", table);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = "dbo.ClientMessageTypeTableType";
            p.AddWithValue("@MaxPerClient", maxPerClient);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ClientMessageType>> GetByClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync("dbo.ClientMessageTypes_GetBy_ClientId", p => p.AddWithValue("@ClientId", clientId),
            reader => new ClientMessageType(
                reader.GetString(reader.GetOrdinal("Type")),
                reader.GetInt64(reader.GetOrdinal("MessageCount")),
                DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("FirstSeenAt")), DateTimeKind.Utc),
                DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal("LastSeenAt")), DateTimeKind.Utc),
                reader.IsDBNull(reader.GetOrdinal("SamplePayload")) ? null : reader.GetString(reader.GetOrdinal("SamplePayload"))),
            cancellationToken);
    }
}
