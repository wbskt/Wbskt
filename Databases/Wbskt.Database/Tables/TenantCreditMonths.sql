-- A tenant's workflow credits for one calendar month (UTC), kept as they are posted
-- (dbo.TenantCreditGrants_Draw): Posted is every credit its workspaces posted to the ledger, Drawn is
-- the part of it taken from its credit grants because it fell past the plan's monthly allowance when it
-- was posted. Posted less Drawn is what the plan itself covered, which is what dbo.Run_Start holds
-- against the allowance. Drawing as credits are posted, under the allowance of the moment, means a
-- downgrade or a lapse never draws on packs for usage the plan already covered.
CREATE TABLE dbo.TenantCreditMonths
(
    TenantId   INT           NOT NULL,
    MonthStart DATE          NOT NULL,
    Posted     DECIMAL(18,4) NOT NULL,
    Drawn      DECIMAL(18,4) NOT NULL CONSTRAINT DF_TenantCreditMonths_Drawn DEFAULT 0,
    UpdatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditMonths_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditMonths_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantCreditMonths PRIMARY KEY (TenantId, MonthStart),
    CONSTRAINT CK_TenantCreditMonths_Drawn CHECK (Drawn >= 0 AND Drawn <= Posted)
);
GO
