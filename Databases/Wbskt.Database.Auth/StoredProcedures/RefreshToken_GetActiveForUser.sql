-- A user's live sessions, newest sign-in first. One row per session: rotating a token revokes the old
-- row, so only the current link of each chain is live. Created is when that link was issued, which
-- is when the session was last used; SessionStarted is when the user signed in.
CREATE PROCEDURE dbo.RefreshToken_GetActiveForUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SessionId,
        SessionStarted,
        Created,
        Expires,
        CreatedByIp
    FROM dbo.RefreshTokens
    WHERE UserId = @UserId
      AND Revoked IS NULL
      AND Expires > SYSUTCDATETIME()
    ORDER BY SessionStarted DESC, Id DESC;
END
GO
