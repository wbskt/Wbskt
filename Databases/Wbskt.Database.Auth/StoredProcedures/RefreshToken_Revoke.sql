-- Revokes a single refresh token (logout). Returns one row: the number of rows actually revoked (0
-- means the token was unknown or already revoked) and the session it belonged to, so the caller can
-- end that session's access tokens too. Rotation does not use this; see dbo.RefreshToken_Rotate.
-- ReplacedByTokenHash is left alone: only rotation sets it, and a signed-out token that carried one
-- would read as rotated away, so presenting it again would end every session as a replay.
-- @ReplacedByTokenHash is accepted and ignored for one release, because a host still on the code before
-- this one passes it in the minutes between the migration and its restart (#997).
CREATE PROCEDURE dbo.RefreshToken_Revoke
    @TokenHash VARBINARY(32),
    @RevokedByIp NVARCHAR(50) = NULL,
    @ReplacedByTokenHash VARBINARY(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Revoked TABLE (SessionId UNIQUEIDENTIFIER NOT NULL);

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    OUTPUT INSERTED.SessionId INTO @Revoked
    WHERE TokenHash = @TokenHash
      AND Revoked IS NULL;

    SELECT
        (SELECT COUNT(*) FROM @Revoked) AS RevokedCount,
        (SELECT TOP (1) SessionId FROM @Revoked) AS SessionId;
END
GO
