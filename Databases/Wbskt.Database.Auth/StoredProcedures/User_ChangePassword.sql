-- Writes a new password for a signed-in user and revokes every refresh token they hold, in one
-- transaction, for the same reason as PasswordResetToken_Consume: changing a password is often a
-- response to someone else having it, and their session must not outlive the change. The service
-- issues the caller a fresh session afterwards.
CREATE PROCEDURE dbo.User_ChangePassword
    @UserId       INT,
    @PasswordHash NVARCHAR(255),
    @RevokedByIp  NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    UPDATE dbo.Users
    SET PasswordHash = @PasswordHash,
        PasswordChangedAt = SYSUTCDATETIME(),
        FailedLoginCount = 0,
        LockedUntil = NULL
    WHERE Id = @UserId;

    UPDATE dbo.RefreshTokens
    SET Revoked = SYSUTCDATETIME(),
        RevokedByIp = @RevokedByIp
    WHERE UserId = @UserId
      AND Revoked IS NULL;

    COMMIT TRANSACTION;
END
GO
