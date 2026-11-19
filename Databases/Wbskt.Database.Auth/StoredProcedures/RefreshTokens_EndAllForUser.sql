-- Revokes every live refresh token of a user, signing them out on every device. Every change that must
-- end the user's sessions (a password change or reset, an email change or revert, deactivation, a
-- replayed token) calls this inside its own transaction. Returns no result set; @RevokedCount is the
-- number revoked.
CREATE PROCEDURE dbo.RefreshTokens_EndAllForUser
    @UserId INT,
    @Now DATETIME2(3) = NULL,
    @RevokedByIp NVARCHAR(50) = NULL,
    @RevokedCount INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.RefreshTokens
    SET Revoked = COALESCE(@Now, SYSUTCDATETIME()),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    SET @RevokedCount = @@ROWCOUNT;
END
GO
