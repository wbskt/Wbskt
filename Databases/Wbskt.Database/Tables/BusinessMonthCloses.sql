-- The months whose Business usage is closed (dbo.BusinessMonth_Close): their tenants and totals in
-- dbo.BusinessTenantMonths are final, and nothing writes them again.
CREATE TABLE dbo.BusinessMonthCloses
(
    MonthStart DATE         NOT NULL,
    ClosedAt   DATETIME2(3) NOT NULL,
    CreatedAt  DATETIME2(3) NOT NULL CONSTRAINT DF_BusinessMonthCloses_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BusinessMonthCloses PRIMARY KEY CLUSTERED (MonthStart)
);
GO
