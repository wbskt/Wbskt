namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>The connections this gateway instance holds, at most one per client.</summary>
public interface IConnectionManager
{
    /// <summary>
    /// Makes <paramref name="connection"/> the client's connection on this host. The newest
    /// connection wins, as it does across hosts: one already held is asked to close as superseded.
    /// </summary>
    void Register(Guid clientRefId, IDeviceConnection connection);

    /// <summary>Forgets <paramref name="connection"/>, unless the client's connection has already been replaced.</summary>
    bool Unregister(Guid clientRefId, IDeviceConnection connection);

    /// <summary>Asks the client's connection on this host, if it has one, to close; false when there is none.</summary>
    Task<bool> RequestCloseAsync(Guid clientRefId, DisconnectReason reason, string description);

    IDeviceConnection? GetConnection(Guid clientRefId);

    /// <summary>A snapshot of every connection held.</summary>
    IReadOnlyCollection<KeyValuePair<Guid, IDeviceConnection>> GetConnections();
}
