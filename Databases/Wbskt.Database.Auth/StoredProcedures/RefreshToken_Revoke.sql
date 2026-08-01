-- Revokes a single refresh token. Returns the number of rows actually revoked: 0 means the token
-- was already revoked, which the service treats as a replay of a token it has previously retired.
CREATE PROCEDURE dbo.RefreshToken_Revoke
    @Token NVARCHAR(255),
    @RevokedByIp NVARCHAR(50) = NULL,
    @ReplacedByToken NVARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp,
        ReplacedByToken = @ReplacedByToken
    WHERE Token = @Token
      AND Revoked IS NULL;

    SELECT @@ROWCOUNT AS RevokedCount;
END
GO
