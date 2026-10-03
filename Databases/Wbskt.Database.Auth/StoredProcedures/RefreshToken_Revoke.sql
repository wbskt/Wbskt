-- Revokes a single refresh token (logout). Returns the number of rows actually revoked: 0 means the
-- token was unknown or already revoked. Rotation does not use this; see dbo.RefreshToken_Rotate.
CREATE PROCEDURE dbo.RefreshToken_Revoke
    @TokenHash VARBINARY(32),
    @RevokedByIp NVARCHAR(50) = NULL,
    @ReplacedByTokenHash VARBINARY(32) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp,
        ReplacedByTokenHash = @ReplacedByTokenHash
    WHERE TokenHash = @TokenHash
      AND Revoked IS NULL;

    SELECT @@ROWCOUNT AS RevokedCount;
END
GO
