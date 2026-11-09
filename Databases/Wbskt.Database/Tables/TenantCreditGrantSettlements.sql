-- The months whose spend past the plan's allowance has been drawn from a tenant's credit grants
-- (dbo.TenantCreditGrants_Settle), so a month is drawn once however often, or by how many replicas,
-- it is settled. Drawn is what the month took from the grants.
CREATE TABLE dbo.TenantCreditGrantSettlements
(
    TenantId   INT           NOT NULL,
    MonthStart DATE          NOT NULL,
    Allowance  DECIMAL(18,4) NOT NULL,
    Spent      DECIMAL(18,4) NOT NULL,
    Drawn      DECIMAL(18,4) NOT NULL,
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditGrantSettlements_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantCreditGrantSettlements PRIMARY KEY (TenantId, MonthStart)
);
GO
