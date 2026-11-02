using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IClientMessageTypeProvider
{
    /// <summary>Adds a batch of tallies. A client's new types stop being stored once it has <paramref name="maxPerClient"/>.</summary>
    Task RecordAsync(IReadOnlyCollection<ClientMessageTypeTally> tallies, int maxPerClient, CancellationToken cancellationToken = default);

    /// <summary>The types a client has sent, most recently seen first.</summary>
    Task<IReadOnlyCollection<ClientMessageType>> GetByClientAsync(int clientId, CancellationToken cancellationToken = default);
}
