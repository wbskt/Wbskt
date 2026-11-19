using System.Collections.Concurrent;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core.Infrastructure;

// In-memory deny-list for revoked client tokens, the fast path in front of the Redis cutoffs (see
// ClientAccessGate). A client's JWT stays valid for up to an hour, so entries must outlive the token
// lifetime.
//
// Each entry is a cutoff: tokens issued before it are refused, with the same rules as
// ClientTokenCutoffs in Redis so the two never disagree. A status revocation or a delete refuses
// everything (cutoff at the end of time); a secret rotation raises the cutoff to the rotation, so the
// client, once given the new secret, can sign in and connect straight away; a re-approval sets it to
// that moment, so the client signs in afresh and a token from before it lost access, an old secret's
// included, stays refused.
//
// An entry past its retention is dropped when it is read, and every few minutes on a write, so a
// client revoked and never seen again does not stay in memory for the life of the host.
internal sealed class RevocationCache : IRevocationCache
{
    // As long as the Redis cutoff it mirrors: a token's lifetime, the clock skew it is accepted with, and a margin.
    internal static readonly TimeSpan RetentionPeriod = ClientTokenCutoffs.Retention;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly TimeProvider _time;
    private long _nextPruneTicks;

    public RevocationCache(TimeProvider time)
    {
        _time = time;
    }

    internal int Count => _entries.Count;

    public void Revoke(Guid clientRefId)
    {
        _entries[clientRefId] = new Entry(DateTime.MaxValue, KeepUntil());
    }

    public void RevokeIssuedBefore(Guid clientRefId, DateTime cutoff)
    {
        // JWT issue times are whole seconds. The cutoff is truncated to the second so a token minted
        // with the new secret in the same second as the rotation is accepted; the cost is that one
        // minted with the old secret earlier in that second is accepted too. That is the same
        // one-second window access-token revocation in the auth host accepts.
        var truncated = TruncateToSecond(cutoff);
        var keepUntil = KeepUntil();
        _entries.AddOrUpdate(
            clientRefId,
            _ => new Entry(truncated, keepUntil),
            (_, existing) => new Entry(existing.Cutoff > truncated ? existing.Cutoff : truncated, keepUntil));
    }

    public void Reinstate(Guid clientRefId, DateTime reinstatedAt)
    {
        _entries[clientRefId] = new Entry(TruncateToSecond(reinstatedAt), KeepUntil());
    }

    public bool IsRevoked(Guid clientRefId, DateTime issuedAt)
    {
        if (!_entries.TryGetValue(clientRefId, out var entry))
        {
            return false;
        }

        if (entry.KeepUntil <= Now())
        {
            // Only the entry read: a revoke written since replaced it and must stay.
            _entries.TryRemove(KeyValuePair.Create(clientRefId, entry));
            return false;
        }

        return issuedAt < entry.Cutoff;
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    // Every write lands here, so pruning rides along with them, at most once per PruneInterval.
    private DateTime KeepUntil()
    {
        var now = Now();
        var due = Interlocked.Read(ref _nextPruneTicks);
        if (now.Ticks >= due && Interlocked.CompareExchange(ref _nextPruneTicks, (now + PruneInterval).Ticks, due) == due)
        {
            foreach (var (clientRefId, entry) in _entries)
            {
                if (entry.KeepUntil <= now)
                {
                    _entries.TryRemove(new KeyValuePair<Guid, Entry>(clientRefId, entry));
                }
            }
        }

        return now.Add(RetentionPeriod);
    }

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private readonly record struct Entry(DateTime Cutoff, DateTime KeepUntil);
}
