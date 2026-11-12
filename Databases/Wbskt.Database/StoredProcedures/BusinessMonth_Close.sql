-- Closes the month from @MonthStart once, for the overage report: each of its Business tenants
-- (dbo.BusinessUsageOf) is written to dbo.BusinessTenantMonths with the month's totals, and the month is
-- marked closed (dbo.BusinessMonths). From then on the report reads the month from there, so a contract
-- that has ended or changed since, or an account copy changed since, does not change what it owes.
-- Closing a closed month does nothing; a month that was never tracked is not closed, since who was on
-- Business in it cannot be known. Returns whether the month is closed.
--
-- A tenant seen on Business only in the month's first @MoveLag and not on Business now is left out: a
-- contract that ended at the end of the last month is seen for a while into this one, until the auth host
-- moves the tenant and the plan copy follows.
--
-- The caller closes a month only once it is over and the message meter has caught up
-- (Billing:BusinessMonthCloseDelay).
CREATE PROCEDURE dbo.BusinessMonth_Close
    @MonthStart DATE,
    @Now        DATETIME2(3),
    @MoveLag    INT = 120 -- minutes
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Resource NVARCHAR(255) = dbo.BusinessMonthLockResource(@MonthStart);
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
    IF @LockResult < 0
        THROW 50035, 'The month''s Business usage is busy; try again.', 1;

    IF EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = @MonthStart AND ClosedAt IS NULL)
    BEGIN
        DELETE b
        FROM dbo.BusinessTenantMonths b
        WHERE b.MonthStart = @MonthStart
          AND b.LastSeenAt < DATEADD(MINUTE, @MoveLag, CAST(@MonthStart AS DATETIME2(3)))
          AND NOT EXISTS (SELECT 1 FROM dbo.BusinessTenantsNow() n WHERE n.TenantId = b.TenantId);

        DECLARE @Usage TABLE
        (
            TenantId       INT              NOT NULL PRIMARY KEY,
            TenantRefId    UNIQUEIDENTIFIER NOT NULL,
            LimitOverrides NVARCHAR(MAX)    NULL,
            MessagesUsed   BIGINT           NOT NULL,
            CreditsPosted  DECIMAL(18,4)    NOT NULL,
            CreditsDrawn   DECIMAL(18,4)    NOT NULL
        );

        INSERT INTO @Usage (TenantId, TenantRefId, LimitOverrides, MessagesUsed, CreditsPosted, CreditsDrawn)
        SELECT
            TenantId,
            TenantRefId,
            LimitOverrides,
            MessagesUsed,
            CreditsPosted,
            CreditsDrawn
        FROM dbo.BusinessUsageOf(@MonthStart);

        UPDATE b
        SET b.MessagesUsed = u.MessagesUsed,
            b.CreditsPosted = u.CreditsPosted,
            b.CreditsDrawn = u.CreditsDrawn
        FROM dbo.BusinessTenantMonths b
        JOIN @Usage u ON u.TenantId = b.TenantId
        WHERE b.MonthStart = @MonthStart;

        INSERT INTO dbo.BusinessTenantMonths (MonthStart, TenantId, TenantRefId, LimitOverrides, MessagesUsed, CreditsPosted, CreditsDrawn)
        SELECT
            @MonthStart,
            u.TenantId,
            u.TenantRefId,
            u.LimitOverrides,
            u.MessagesUsed,
            u.CreditsPosted,
            u.CreditsDrawn
        FROM @Usage u
        WHERE NOT EXISTS (SELECT 1
                          FROM dbo.BusinessTenantMonths b
                          WHERE b.MonthStart = @MonthStart
                            AND b.TenantId = u.TenantId);

        UPDATE dbo.BusinessMonths
        SET ClosedAt = @Now
        WHERE MonthStart = @MonthStart;
    END

    COMMIT TRANSACTION;

    SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = @MonthStart AND ClosedAt IS NOT NULL) THEN 1 ELSE 0 END AS BIT) AS Closed;
END
GO
