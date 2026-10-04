-- Revokes a single refresh token (logout). Returns one row: the number of rows actually revoked (0
-- means the token was unknown or already revoked) and the session it belonged to, so the caller can
-- end that session's access tokens too. Rotation does not use this; see dbo.RefreshToken_Rotate.
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
        RevokedByIp = @RevokedByIp,
        ReplacedByTokenHash = @ReplacedByTokenHash
    OUTPUT INSERTED.SessionId INTO @Revoked
    WHERE TokenHash = @TokenHash
      AND Revoked IS NULL;

    SELECT
        (SELECT COUNT(*) FROM @Revoked) AS RevokedCount,
        (SELECT TOP (1) SessionId FROM @Revoked) AS SessionId;
END
GO
