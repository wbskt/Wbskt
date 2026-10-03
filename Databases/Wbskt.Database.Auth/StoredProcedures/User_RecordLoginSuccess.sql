-- A successful sign-in clears the failure count and any lapsed lock, and stamps LastLoginAt.
CREATE PROCEDURE dbo.User_RecordLoginSuccess
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET FailedLoginCount = 0,
        LockedUntil = NULL,
        LastLoginAt = SYSUTCDATETIME()
    WHERE Id = @UserId;
END
GO
