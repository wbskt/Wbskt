-- Records each tenant on Business now (dbo.BusinessTenantsNow) as on Business in the month holding @Now,
-- with its contract as it stands, so the month's overage report still lists it after the contract ends
-- (dbo.BusinessUsageOf), and marks the month as tracked (dbo.BusinessMonths). Run through the month by
-- BusinessUsageWorker. A closed month is left as it is. Under the month's lock, as dbo.BusinessMonth_Close
-- takes it.
CREATE PROCEDURE dbo.BusinessTenantMonth_Track
    @Now DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @MonthStart DATE = DATEFROMPARTS(YEAR(@Now), MONTH(@Now), 1);

    BEGIN TRANSACTION;

    DECLARE @Resource NVARCHAR(255) = dbo.BusinessMonthLockResource(@MonthStart);
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 50035, 'The month''s Business usage is busy; try again.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = @MonthStart AND ClosedAt IS NOT NULL)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = @MonthStart)
            INSERT INTO dbo.BusinessMonths (MonthStart, TrackedFrom) VALUES (@MonthStart, @Now);

        MERGE dbo.BusinessTenantMonths AS t
        USING dbo.BusinessTenantsNow() AS s
        ON t.MonthStart = @MonthStart AND t.TenantId = s.TenantId
        WHEN MATCHED THEN
            UPDATE SET TenantRefId = s.TenantRefId,
                       LimitOverrides = s.LimitOverrides,
                       LastSeenAt = @Now
        WHEN NOT MATCHED THEN
            INSERT (MonthStart, TenantId, TenantRefId, LimitOverrides, FirstSeenAt, LastSeenAt)
            VALUES (@MonthStart, s.TenantId, s.TenantRefId, s.LimitOverrides, @Now, @Now);
    END

    COMMIT TRANSACTION;
END
GO
