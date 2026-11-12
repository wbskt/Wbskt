-- The months whose Business usage is kept for the overage report: TrackedFrom is when
-- dbo.BusinessTenantMonth_Track first recorded the month's tenants, and ClosedAt when
-- dbo.BusinessMonth_Close made its tenants and totals in dbo.BusinessTenantMonths final. A month with
-- no row was never tracked (before the tracker ran) and is never closed: the report works it out as it
-- stands every time it is asked.
CREATE TABLE dbo.BusinessMonths
(
    MonthStart  DATE         NOT NULL,
    TrackedFrom DATETIME2(3) NOT NULL,
    ClosedAt    DATETIME2(3) NULL,
    CreatedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_BusinessMonths_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BusinessMonths PRIMARY KEY CLUSTERED (MonthStart)
);
GO
