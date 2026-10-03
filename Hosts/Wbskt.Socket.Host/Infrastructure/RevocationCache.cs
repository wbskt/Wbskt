using System.Collections.Concurrent;

namespace Wbskt.Socket.Host.Infrastructure;

// In-memory deny-list for revoked client tokens. A client's JWT stays valid for up to an hour, so
// the auth middleware alone cannot stop the SDK's auto-reconnect loop; entries must outlive the
// token lifetime. Single-node, same constraint as ConnectionManager.
//
// Each entry is a cutoff: tokens issued before it are refused. A status revocation or a delete
// refuses everything (cutoff at the end of time); a secret rotation refuses only tokens issued
// before it, so the device, once given the new secret, can sign in and connect straight away.
internal sealed class RevocationCache : IRevocationCache
{
    // Client JWT lifetime (60 min) + margin.
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromMinutes(70);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public void Revoke(Guid clientRefId)
    {
        _entries[clientRefId] = new Entry(DateTime.MaxValue, DateTime.UtcNow.Add(RetentionPeriod));
    }

    public void RevokeIssuedBefore(Guid clientRefId, DateTime cutoff)
    {
        // JWT issue times are whole seconds. The cutoff is truncated to the second so a token minted
        // with the new secret in the same second as the rotation is accepted; the cost is that one
        // minted with the old secret earlier in that second is accepted too. That is the same
        // one-second window access-token revocation in the auth host accepts.
        var truncated = new DateTime(cutoff.Ticks - cutoff.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var keepUntil = DateTime.UtcNow.Add(RetentionPeriod);
        _entries.AddOrUpdate(
            clientRefId,
            _ => new Entry(truncated, keepUntil),
            (_, existing) => new Entry(existing.Cutoff > truncated ? existing.Cutoff : truncated, keepUntil));
    }

    public void Clear(Guid clientRefId)
    {
        _entries.TryRemove(clientRefId, out _);
    }

    public bool IsRevoked(Guid clientRefId, DateTime issuedAt)
    {
        if (!_entries.TryGetValue(clientRefId, out var entry))
        {
            return false;
        }

        if (entry.KeepUntil <= DateTime.UtcNow)
        {
            _entries.TryRemove(clientRefId, out _);
            return false;
        }

        return issuedAt < entry.Cutoff;
    }

    private readonly record struct Entry(DateTime Cutoff, DateTime KeepUntil);
}
