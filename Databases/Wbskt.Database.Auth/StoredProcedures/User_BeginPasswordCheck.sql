-- Reserves one password attempt against the account before the password is checked, and says whether
-- it may be checked at all. The attempt counts as a failure until dbo.User_EndPasswordCheck settles it.
--
-- One statement, so parallel guesses cannot all pass the lock check before any of them is counted:
-- each takes a slot, and once @MaxFailures attempts are counted or in flight the rest are refused.
-- A locked account is refused too, unless @ThroughLock (a browser the account signed in from
-- before, whose password is still tried through a lock). The lock is compared with the database's
-- clock, the one that set it.
CREATE PROCEDURE dbo.User_BeginPasswordCheck
    @UserId      INT,
    @MaxFailures INT,
    @ThroughLock BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Users
    SET FailedLoginCount = FailedLoginCount + 1
    WHERE Id = @UserId
      AND FailedLoginCount < @MaxFailures
      AND (@ThroughLock = 1 OR LockedUntil IS NULL OR LockedUntil <= SYSUTCDATETIME());

    SELECT CAST(CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END AS BIT) AS Reserved;
END
GO
