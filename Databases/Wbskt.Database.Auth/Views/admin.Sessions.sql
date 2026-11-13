-- One row per sign-in session: the newest refresh token of each session, without its hash. A session
-- is live while that token is neither revoked nor expired.
CREATE VIEW [admin].Sessions
AS
SELECT
    RT.SessionId,
    RT.UserId,
    RT.SessionStarted AS StartedAt,
    RT.Created        AS LastRefreshedAt,
    RT.Expires        AS ExpiresAt,
    RT.Revoked        AS RevokedAt,
    RT.CreatedByIp    AS IpAddress
FROM dbo.RefreshTokens RT
WHERE RT.ReplacedByTokenHash IS NULL;
GO
