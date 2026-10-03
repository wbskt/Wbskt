-- Counts a wrong password against the account and locks it once the count reaches @MaxFailures.
-- Locking resets the count, so after the lock lapses the account gets a fresh @MaxFailures tries.
--
-- One statement, so concurrent failures each count: a read-then-write from the service would let a
-- burst of parallel guesses all read the same count and only advance it by one.
CREATE PROCEDURE dbo.User_RecordLoginFailure
    @UserId         INT,
    @MaxFailures    INT,
    @LockoutSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET LockedUntil = CASE WHEN FailedLoginCount + 1 >= @MaxFailures
                           THEN DATEADD(SECOND, @LockoutSeconds, SYSUTCDATETIME())
                           ELSE LockedUntil END,
        FailedLoginCount = CASE WHEN FailedLoginCount + 1 >= @MaxFailures
                                THEN 0
                                ELSE FailedLoginCount + 1 END
    OUTPUT inserted.LockedUntil
    WHERE Id = @UserId;
END
GO
