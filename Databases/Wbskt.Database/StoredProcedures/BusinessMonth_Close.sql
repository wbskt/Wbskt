-- Closes the month from @MonthStart once, for the overage report: each of its Business tenants
-- (dbo.BusinessUsageOf) is written to dbo.BusinessTenantMonths with the month's totals, and the month to
-- dbo.BusinessMonthCloses. From then on the report reads the month from there, so a contract that has
-- ended or changed since, or an account copy changed since, does not change what the month owes.
-- Closing a closed month does nothing. The caller closes a month only once it is over and the meters
-- have caught up (Billing:BusinessMonthCloseDelay).
CREATE PROCEDURE dbo.BusinessMonth_Close
    @MonthStart DATE,
    @Now        DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    DECLARE @Resource NVARCHAR(255) = CONCAT(N'business-month:', CONVERT(NCHAR(10), @MonthStart, 23));
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 50035, 'The month''s Business usage is busy; try again.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.BusinessMonthCloses WHERE MonthStart = @MonthStart)
    BEGIN
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

        INSERT INTO dbo.BusinessMonthCloses (MonthStart, ClosedAt)
        VALUES (@MonthStart, @Now);
    END

    COMMIT TRANSACTION;
END
GO
