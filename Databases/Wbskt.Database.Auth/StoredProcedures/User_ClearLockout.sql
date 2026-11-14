-- Lifts a sign-in lockout and clears the failure count, for staff on the person's behalf. The same
-- columns a successful sign-in clears (dbo.User_RecordLoginSuccess), without stamping a sign-in.
CREATE PROCEDURE dbo.User_ClearLockout
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET FailedLoginCount = 0,
        LockedUntil = NULL
    WHERE Id = @Id;
END
GO
