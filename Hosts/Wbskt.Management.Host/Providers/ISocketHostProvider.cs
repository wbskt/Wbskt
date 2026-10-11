namespace Wbskt.Management.Host.Providers;

/// <summary>The socket hosts the management host has heard from (dbo.SocketHosts).</summary>
public interface ISocketHostProvider
{
    /// <summary>Records that <paramref name="hostId"/> is alive, at the database's time.</summary>
    Task RecordHeartbeatAsync(string hostId, CancellationToken cancellationToken = default);
}
