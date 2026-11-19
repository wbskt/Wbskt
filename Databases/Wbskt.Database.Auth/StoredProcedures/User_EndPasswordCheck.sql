-- Settles an attempt dbo.User_BeginPasswordCheck reserved. A right password gives the slot back. A
-- wrong one leaves it counted, and when the count has reached @MaxFailures locks the account and
-- starts the count again, so after the lock lapses it gets a fresh @MaxFailures tries.
--
-- Returns one row: LockedUntil when this call locked the account, otherwise null.
CREATE PROCEDURE dbo.User_EndPasswordCheck
    @UserId         INT,
    @Succeeded      BIT,
    @MaxFailures    INT,
    @LockoutSeconds INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Locked TABLE (LockedUntil DATETIME2(3) NOT NULL);

    IF @Succeeded = 1
    BEGIN
        UPDATE dbo.Users
        SET FailedLoginCount = FailedLoginCount - 1
        WHERE Id = @UserId
          AND FailedLoginCount > 0;
    END
    ELSE
    BEGIN
        UPDATE dbo.Users
        SET LockedUntil = DATEADD(SECOND, @LockoutSeconds, SYSUTCDATETIME()),
            FailedLoginCount = 0
        OUTPUT inserted.LockedUntil INTO @Locked
        WHERE Id = @UserId
          AND FailedLoginCount >= @MaxFailures;
    END

    SELECT (SELECT TOP (1) LockedUntil FROM @Locked) AS LockedUntil;
END
GO
