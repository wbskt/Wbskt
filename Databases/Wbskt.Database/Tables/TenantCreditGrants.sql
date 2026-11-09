-- Workflow credits granted to a tenant on top of its plan's monthly allowance: a credit pack bought
-- (Source 'dodo', SourceRef the payment) or one an operator gave. Each is spendable until ExpiresAt.
-- What a month spends past the plan's allowance is drawn from the grants live at the time, soonest to
-- expire first, as it is posted (dbo.TenantCreditGrants_Draw); dbo.Run_Start counts the grants' undrawn
-- credits on top of the allowance. A refund revokes a grant (RevokedAt): what is undrawn is
-- gone, what was drawn stays drawn. While a chargeback on its payment is open the grant is held
-- (HeldAt): its undrawn credits do not count until it is released. HoldRevision is the auth database's,
-- so a hold or release delivered late never undoes a newer one. ExpiryWarnedAt is when the tenant was
-- told the grant runs out soon (dbo.TenantCreditGrants_ClaimExpiring). Rows are never deleted.
CREATE TABLE dbo.TenantCreditGrants
(
    Id           INT           IDENTITY(1, 1) NOT NULL,
    TenantId     INT           NOT NULL,
    Credits      DECIMAL(18,4) NOT NULL,
    Drawn        DECIMAL(18,4) NOT NULL CONSTRAINT DF_TenantCreditGrants_Drawn DEFAULT 0,
    GrantedAt    DATETIME2(3)  NOT NULL,
    ExpiresAt    DATETIME2(3)  NOT NULL,
    Source       NVARCHAR(40)  NOT NULL,
    SourceRef    NVARCHAR(200) NOT NULL,
    RevokedAt    DATETIME2(3)  NULL,
    HeldAt       DATETIME2(3)  NULL,
    HoldRevision INT           NOT NULL CONSTRAINT DF_TenantCreditGrants_HoldRevision DEFAULT 0,
    ExpiryWarnedAt DATETIME2(3) NULL,
    CreatedAt    DATETIME2(3)  NOT NULL CONSTRAINT DF_TenantCreditGrants_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TenantCreditGrants PRIMARY KEY (Id),
    CONSTRAINT UQ_TenantCreditGrants_Source UNIQUE (Source, SourceRef),
    CONSTRAINT CK_TenantCreditGrants_Credits CHECK (Credits > 0 AND Drawn >= 0 AND Drawn <= Credits)
);
GO

CREATE NONCLUSTERED INDEX IX_TenantCreditGrants_TenantId
    ON dbo.TenantCreditGrants (TenantId, ExpiresAt) INCLUDE (Credits, Drawn, RevokedAt, HeldAt);
GO
