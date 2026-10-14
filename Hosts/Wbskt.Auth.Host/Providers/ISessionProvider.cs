using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;

namespace Wbskt.Auth.Host.Providers;

/// <summary>
/// Sessions: the refresh tokens a sign-in starts, rotation keeps alive and sign-out ends. Tokens are
/// stored as hashes; the raw value never reaches the database.
/// </summary>
internal interface ISessionProvider
{
    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Revokes one live token. Returns its session, or null when it was unknown or already revoked.</summary>
    Task<Guid?> RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires <paramref name="token"/> and stores <paramref name="replacement"/> in one transaction
    /// (<c>dbo.RefreshToken_Rotate</c>). A replayed token revokes every session the user has in the
    /// same call; see <see cref="RefreshRotationOutcome"/> for the other answers.
    /// </summary>
    /// <remarks>A session that started more than <paramref name="sessionLifetime"/> ago is not extended.</remarks>
    Task<RefreshRotation> RotateRefreshTokenAsync(string token, RefreshToken replacement, TimeSpan sessionLifetime, string ipAddress, CancellationToken cancellationToken = default);

    Task<int> RevokeAllRefreshTokensForUserAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SessionResponse>> GetActiveSessionsAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Revokes one of the user's own live sessions. Returns 0 when there is no such live session of theirs.</summary>
    Task<int> RevokeSessionAsync(Guid sessionId, int userId, string? revokedByIp, CancellationToken cancellationToken = default);
}
