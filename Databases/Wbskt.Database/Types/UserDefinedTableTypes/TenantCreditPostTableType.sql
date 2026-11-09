-- Credits just posted to the ledger for each tenant, with the tenant's monthly allowance at that moment
-- (dbo.WorkspaceAccounts.CreditsPerMonth): what dbo.TenantCreditGrants_Draw draws from its grants.
CREATE TYPE dbo.TenantCreditPostTableType AS TABLE
(
    TenantId  INT           NOT NULL PRIMARY KEY,
    Credits   DECIMAL(18,4) NOT NULL,
    Allowance DECIMAL(18,4) NOT NULL
);
GO
