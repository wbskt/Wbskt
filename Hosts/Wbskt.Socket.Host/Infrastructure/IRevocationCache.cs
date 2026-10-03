namespace Wbskt.Socket.Host.Infrastructure;

public interface IRevocationCache
{
    /// <summary>Refuses every token the client holds now (its status left Registered, or it was deleted).</summary>
    void Revoke(Guid clientRefId);

    /// <summary>Refuses tokens issued before <paramref name="cutoff"/> (its secret was rotated).</summary>
    void RevokeIssuedBefore(Guid clientRefId, DateTime cutoff);

    void Clear(Guid clientRefId);

    /// <summary>Whether a token for the client issued at <paramref name="issuedAt"/> (UTC) is refused.</summary>
    bool IsRevoked(Guid clientRefId, DateTime issuedAt);
}
