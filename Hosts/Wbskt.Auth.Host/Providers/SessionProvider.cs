using System.Data;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Providers;

internal sealed class SessionProvider : BaseSqlProvider, ISessionProvider
{
    public SessionProvider(IConfiguration configuration) : base(configuration, "AuthDBConnection") { }

    public async Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default)
    {
        var parameters = await ExecuteNonQueryAsync("dbo.RefreshToken_Insert", p =>
        {
            p.AddWithValue("@UserId", token.UserId);
            p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token.Token);
            p.AddWithValue("@Expires", token.Expires);
            p.AddWithValue("@CreatedByIp", ipAddress ?? (object)DBNull.Value);
            p.AddWithValue("@SessionId", token.SessionId);
            p.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        }, cancellationToken);

        token.Id = (int)parameters["@Id"].Value;
    }

    public async Task<Guid?> RevokeRefreshTokenAsync(string token, string ipAddress, string? replacedByToken, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync<Guid?>(
            "dbo.RefreshToken_Revoke",
            p =>
            {
                p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token);
                p.AddWithValue("@RevokedByIp", ipAddress ?? (object)DBNull.Value);
                p.Add("@ReplacedByTokenHash", SqlDbType.VarBinary, 32).Value =
                    replacedByToken is null ? DBNull.Value : SecurityTokens.Hash(replacedByToken);
            },
            reader => reader.IsDBNull(reader.GetOrdinal("SessionId")) ? null : reader.GetGuid(reader.GetOrdinal("SessionId")),
            cancellationToken: cancellationToken);
    }

    public async Task<RefreshRotation> RotateRefreshTokenAsync(string token, RefreshToken replacement, TimeSpan sessionLifetime, string ipAddress, CancellationToken cancellationToken = default)
    {
        return await ExecuteSingleAsync(
            "dbo.RefreshToken_Rotate",
            p =>
            {
                p.Add("@TokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(token);
                p.Add("@NewTokenHash", SqlDbType.VarBinary, 32).Value = SecurityTokens.Hash(replacement.Token);
                p.AddWithValue("@NewExpires", replacement.Expires);
                p.AddWithValue("@SessionLifetimeSeconds", (int)sessionLifetime.TotalSeconds);
                p.AddWithValue("@Ip", ipAddress ?? (object)DBNull.Value);
            },
            reader =>
            {
                var outcome = Enum.Parse<RefreshRotationOutcome>(reader.GetString(reader.GetOrdinal("Outcome")));
                var userIdOrdinal = reader.GetOrdinal("UserId");
                int? userId = reader.IsDBNull(userIdOrdinal) ? null : reader.GetInt32(userIdOrdinal);
                var sessionIdOrdinal = reader.GetOrdinal("SessionId");
                Guid? sessionId = reader.IsDBNull(sessionIdOrdinal) ? null : reader.GetGuid(sessionIdOrdinal);

                User? user = null;
                if (outcome == RefreshRotationOutcome.Rotated && userId is not null)
                {
                    user = new User
                    {
                        Id = userId.Value,
                        RefId = reader.GetGuid(reader.GetOrdinal("RefId")),
                        Username = reader.GetString(reader.GetOrdinal("Username")),
                        Email = reader.GetString(reader.GetOrdinal("Email")),
                        IsActive = true
                    };
                }

                return new RefreshRotation(outcome, userId, user, sessionId);
            },
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> RevokeAllRefreshTokensForUserAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RefreshToken_RevokeAllForUser", p =>
        {
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@RevokedByIp", ipAddress ?? (object)DBNull.Value);
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SessionResponse>> GetActiveSessionsAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.RefreshToken_GetActiveForUser",
            p => p.AddWithValue("@UserId", userId),
            r => new SessionResponse(
                r.GetGuid(r.GetOrdinal("SessionId")),
                r.GetDateTime(r.GetOrdinal("SessionStarted")),
                r.GetDateTime(r.GetOrdinal("Created")),
                r.GetDateTime(r.GetOrdinal("Expires")),
                r.IsDBNull(r.GetOrdinal("CreatedByIp")) ? null : r.GetString(r.GetOrdinal("CreatedByIp"))),
            cancellationToken);
    }

    public async Task<int> RevokeSessionAsync(Guid sessionId, int userId, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>("dbo.RefreshToken_RevokeForUser", p =>
        {
            p.AddWithValue("@SessionId", sessionId);
            p.AddWithValue("@UserId", userId);
            p.AddWithValue("@RevokedByIp", (object?)revokedByIp ?? DBNull.Value);
        }, cancellationToken);
    }
}
