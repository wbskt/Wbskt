using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

/// <summary>The socket hosts the management host has heard from (dbo.SocketHosts).</summary>
public interface ISocketHostProvider
{
    /// <summary>
    /// Records that <paramref name="hostId"/> is alive, at the database's time. When it had been silent
    /// for longer than <paramref name="silentAfterSeconds"/>, returns the clients it holds that have
    /// commands waiting; otherwise, or with no threshold, none.
    /// </summary>
    Task<IReadOnlyCollection<SilentHostClient>> RecordHeartbeatAsync(string hostId, int? silentAfterSeconds = null, CancellationToken cancellationToken = default);
}
