-- Revokes every live refresh token for a user. Used by sign-out-everywhere, by account
-- deactivation, and when a retired token is replayed (which implies the token has leaked).
CREATE PROCEDURE dbo.RefreshToken_RevokeAllForUser
    @UserId INT,
    @RevokedByIp NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    SELECT @@ROWCOUNT AS RevokedCount;
END
GO
