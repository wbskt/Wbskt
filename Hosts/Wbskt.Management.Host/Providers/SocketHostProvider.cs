using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

internal sealed class SocketHostProvider : BaseSqlProvider, ISocketHostProvider
{
    public SocketHostProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<SilentHostClient>> RecordHeartbeatAsync(string hostId, int? silentAfterSeconds = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.SocketHost_RecordHeartbeat",
            p =>
            {
                p.AddWithValue("@HostId", hostId);
                p.AddWithValue("@SilentAfterSeconds", (object?)silentAfterSeconds ?? DBNull.Value);
            },
            reader => new SilentHostClient(
                reader.GetInt32(reader.GetOrdinal("ClientId")),
                reader.GetGuid(reader.GetOrdinal("ClientRefId")),
                reader.GetInt32(reader.GetOrdinal("WorkspaceId"))),
            cancellationToken);
    }
}
