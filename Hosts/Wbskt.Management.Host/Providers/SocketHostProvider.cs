using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Providers;

internal sealed class SocketHostProvider : BaseSqlProvider, ISocketHostProvider
{
    public SocketHostProvider(IConfiguration configuration) : base(configuration) { }

    public async Task RecordHeartbeatAsync(string hostId, CancellationToken cancellationToken = default)
    {
        await ExecuteNonQueryAsync("dbo.SocketHost_RecordHeartbeat", p =>
        {
            p.AddWithValue("@HostId", hostId);
        }, cancellationToken);
    }
}
