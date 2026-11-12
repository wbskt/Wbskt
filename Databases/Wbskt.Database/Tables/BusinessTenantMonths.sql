-- Each tenant on Business during a calendar month (UTC), kept for the overage report
-- (dbo.BusinessUsage_GetBy_Month), so a contract that ends during the month or at its end is still billed
-- for it after its tenant has moved off Business. BusinessTenantMonth_Track writes the tenants on Business
-- now through the month, with the contract (LimitOverrides) as last seen (LastSeenAt, NULL for a tenant
-- added only at the close). BusinessMonth_Close writes the month's totals once the month is over
-- (dbo.BusinessMonthCloses), and the report reads them from here from then on.
CREATE TABLE dbo.BusinessTenantMonths
(
    MonthStart     DATE             NOT NULL,
    TenantId       INT              NOT NULL,
    TenantRefId    UNIQUEIDENTIFIER NOT NULL,
    LimitOverrides NVARCHAR(MAX)    NULL,
    LastSeenAt     DATETIME2(3)     NULL,
    MessagesUsed   BIGINT           NULL,
    CreditsPosted  DECIMAL(18,4)    NULL,
    CreditsDrawn   DECIMAL(18,4)    NULL,
    CreatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_BusinessTenantMonths_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BusinessTenantMonths PRIMARY KEY CLUSTERED (MonthStart, TenantId)
);
GO
