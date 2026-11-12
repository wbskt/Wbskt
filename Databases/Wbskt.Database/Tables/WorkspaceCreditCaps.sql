-- A workspace's optional limit on the workflow credits it may spend in a calendar month (UTC), out of
-- its tenant's shared allowance. A ceiling, not a reservation: the caps of a tenant's workspaces may add
-- up to more than its allowance. No row is no cap. Set by WorkspaceCreditCap_Set; Run_Start holds it
-- exactly by counting what is spent and the unspent budgets of the workspace's running runs.
CREATE TABLE dbo.WorkspaceCreditCaps
(
    WorkspaceId      INT           NOT NULL,
    MonthlyCreditCap DECIMAL(18,4) NOT NULL,
    UpdatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditCaps_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditCaps_CreatedAt DEFAULT SYSUTCDATETIME(),
    -- The highest share of the cap (0, 80 or 100) the workspace has been told it reached in the month
    -- starting NotifiedMonthStart; another month counts as 0. Raised by WorkspaceCreditCap_MarkNotified
    -- only after the notice is published (see CreditNotices_GetDue).
    NotifiedMonthStart DATE        NULL,
    NotifiedPercent  INT           NOT NULL CONSTRAINT DF_WorkspaceCreditCaps_NotifiedPercent DEFAULT 0,
    CONSTRAINT PK_WorkspaceCreditCaps PRIMARY KEY (WorkspaceId),
    CONSTRAINT CK_WorkspaceCreditCaps_MonthlyCreditCap CHECK (MonthlyCreditCap >= 0)
);
GO
