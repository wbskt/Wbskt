-- A user's live sessions, newest first. One row per session: rotating a token revokes the old row,
-- so only the current link of each chain is live.
CREATE PROCEDURE dbo.RefreshToken_GetActiveForUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        Created,
        Expires,
        CreatedByIp
    FROM dbo.RefreshTokens
    WHERE UserId = @UserId
      AND Revoked IS NULL
      AND Expires > SYSUTCDATETIME()
    ORDER BY Created DESC, Id DESC;
END
GO
