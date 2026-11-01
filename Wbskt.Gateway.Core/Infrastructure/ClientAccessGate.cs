using Wbskt.Infrastructure.Security;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// The one place a gateway decides whether a device may be connected: the gateway-side twin of the
/// management host's ClientAccessRevoker, with the same three verbs.
/// </summary>
public interface IClientAccessGate
{
    /// <summary>Refuses every token the device holds and closes its connection (status left Registered, or deleted).</summary>
    Task RevokeAsync(Guid clientRefId, string description, CancellationToken cancellationToken = default);

    /// <summary>Refuses tokens issued before <paramref name="cutoff"/> and closes its connection, opened with one of them (secret rotated).</summary>
    Task RevokeIssuedBeforeAsync(Guid clientRefId, DateTime cutoff, string description, CancellationToken cancellationToken = default);

    /// <summary>The device may connect again with a token issued from <paramref name="reinstatedAt"/> on (approved again).</summary>
    void Reinstate(Guid clientRefId, DateTime reinstatedAt);

    /// <summary>Whether a token for the device issued at <paramref name="issuedAt"/> (UTC) is refused.</summary>
    Task<bool> IsRefusedAsync(Guid clientRefId, DateTime issuedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this host's own list, which every revoke updates before it closes the device's
    /// connection, refuses the token. No round trip, so it is safe to ask on every connect.
    /// </summary>
    bool IsRefusedHere(Guid clientRefId, DateTime issuedAt);
}

/// <summary>
/// This host's own list (filled by events, lost on restart) answers first; the cutoffs the management
/// host keeps in Redis are asked next, and the list decides alone only when Redis cannot answer. Both
/// follow the same cutoff rules, so whichever answers, the answer is the same.
/// </summary>
internal sealed class ClientAccessGate : IClientAccessGate
{
    private readonly IRevocationCache _cache;
    private readonly IClientTokenCutoffs _cutoffs;
    private readonly IConnectionManager _connections;

    public ClientAccessGate(IRevocationCache cache, IClientTokenCutoffs cutoffs, IConnectionManager connections)
    {
        _cache = cache;
        _cutoffs = cutoffs;
        _connections = connections;
    }

    public async Task RevokeAsync(Guid clientRefId, string description, CancellationToken cancellationToken = default)
    {
        _cache.Revoke(clientRefId);
        await _connections.RequestCloseAsync(clientRefId, DisconnectReason.Revoked, description);
    }

    public async Task RevokeIssuedBeforeAsync(Guid clientRefId, DateTime cutoff, string description, CancellationToken cancellationToken = default)
    {
        _cache.RevokeIssuedBefore(clientRefId, cutoff);
        await _connections.RequestCloseAsync(clientRefId, DisconnectReason.Revoked, description);
    }

    public void Reinstate(Guid clientRefId, DateTime reinstatedAt)
    {
        _cache.Reinstate(clientRefId, reinstatedAt);
    }

    public async Task<bool> IsRefusedAsync(Guid clientRefId, DateTime issuedAt, CancellationToken cancellationToken = default)
    {
        return _cache.IsRevoked(clientRefId, issuedAt)
               || await _cutoffs.IsRevokedAsync(clientRefId, issuedAt, cancellationToken) == true;
    }

    public bool IsRefusedHere(Guid clientRefId, DateTime issuedAt) => _cache.IsRevoked(clientRefId, issuedAt);
}
