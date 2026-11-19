-- Reserves one password attempt against the account before the password is checked, and says whether
-- it may be checked at all. The attempt counts as a failure until dbo.User_EndPasswordCheck settles it.
--
-- One statement, so parallel guesses cannot all pass the lock check before any of them is counted:
-- each takes a slot, and once @MaxFailures attempts are counted or in flight the rest are refused.
-- A locked account is refused too, unless @ThroughLock (a browser the account signed in from
-- before, whose password is still tried through a lock). The lock is compared with the database's
-- clock, the one that set it.
--
-- A refused attempt that finds the count at the limit with no lock in force, and no attempt reserved
-- in the last two minutes, turns the count into a lock for @LockoutSeconds. Without that, attempts never
-- settled (a host stopped between the two calls) would refuse the account for good; with it they lock
-- it like wrong passwords, and it lapses. Attempts reserved more recently may still be settled: they
-- are refused, and a wrong one settled at the limit locks the account and tells its owner, as usual.
CREATE PROCEDURE dbo.User_BeginPasswordCheck
    @UserId         INT,
    @MaxFailures    INT,
    @ThroughLock    BIT = 0,

    -- Optional only so the previous image keeps working while the hosts restart after the migration;
    -- required in the release after (#1059).
    @LockoutSeconds INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET FailedLoginCount = FailedLoginCount + 1,
        PasswordCheckReservedAt = SYSUTCDATETIME()
    WHERE Id = @UserId
      AND FailedLoginCount < @MaxFailures
      AND (@ThroughLock = 1 OR LockedUntil IS NULL OR LockedUntil <= SYSUTCDATETIME());

    DECLARE @Reserved BIT = CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END;

    IF @Reserved = 0 AND @LockoutSeconds IS NOT NULL
    BEGIN
        UPDATE dbo.Users
        SET LockedUntil = DATEADD(SECOND, @LockoutSeconds, SYSUTCDATETIME()),
            FailedLoginCount = 0
        WHERE Id = @UserId
          AND FailedLoginCount >= @MaxFailures
          AND (LockedUntil IS NULL OR LockedUntil <= SYSUTCDATETIME())
          AND (PasswordCheckReservedAt IS NULL OR PasswordCheckReservedAt < DATEADD(MINUTE, -2, SYSUTCDATETIME()));
    END

    SELECT @Reserved AS Reserved;
END
GO
