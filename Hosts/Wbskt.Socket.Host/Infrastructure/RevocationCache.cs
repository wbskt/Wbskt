using System.Collections.Concurrent;

namespace Wbskt.Socket.Host.Infrastructure;

// In-memory deny-list for revoked clients. A revoked client's JWT stays valid for up to an hour,
// so the auth middleware alone cannot stop the SDK's auto-reconnect loop; entries must outlive
// the token lifetime. Single-node, same constraint as ConnectionManager.
internal sealed class RevocationCache : IRevocationCache
{
    // Client JWT lifetime (60 min) + margin.
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromMinutes(70);

    private readonly ConcurrentDictionary<Guid, DateTime> _revokedUntil = new();

    public void Revoke(Guid clientRefId)
    {
        _revokedUntil[clientRefId] = DateTime.UtcNow.Add(RetentionPeriod);
    }

    public void Clear(Guid clientRefId)
    {
        _revokedUntil.TryRemove(clientRefId, out _);
    }

    public bool IsRevoked(Guid clientRefId)
    {
        if (!_revokedUntil.TryGetValue(clientRefId, out var until))
        {
            return false;
        }

        if (until <= DateTime.UtcNow)
        {
            _revokedUntil.TryRemove(clientRefId, out _);
            return false;
        }

        return true;
    }
}
