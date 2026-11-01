namespace Wbskt.Gateway.Core.Infrastructure;

public interface IRevocationCache
{
    /// <summary>Refuses every token the client holds now (its status left Registered, or it was deleted).</summary>
    void Revoke(Guid clientRefId);

    /// <summary>Refuses tokens issued before <paramref name="cutoff"/> (its secret was rotated).</summary>
    void RevokeIssuedBefore(Guid clientRefId, DateTime cutoff);

    /// <summary>Refuses tokens issued before <paramref name="reinstatedAt"/> only (it was approved again).</summary>
    void Reinstate(Guid clientRefId, DateTime reinstatedAt);

    /// <summary>Whether a token for the client issued at <paramref name="issuedAt"/> (UTC) is refused.</summary>
    bool IsRevoked(Guid clientRefId, DateTime issuedAt);
}
