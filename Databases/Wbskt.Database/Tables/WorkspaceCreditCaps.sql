-- A workspace's optional limit on the workflow credits it may spend in a calendar month (UTC), out of
-- its tenant's shared allowance. A ceiling, not a reservation: the caps of a tenant's workspaces may add
-- up to more than its allowance. No row is no cap. Set by WorkspaceCreditCap_Set; checked in Run_Start.
CREATE TABLE dbo.WorkspaceCreditCaps
(
    WorkspaceId      INT           NOT NULL,
    MonthlyCreditCap DECIMAL(18,4) NOT NULL,
    UpdatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditCaps_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditCaps_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceCreditCaps PRIMARY KEY (WorkspaceId),
    CONSTRAINT CK_WorkspaceCreditCaps_MonthlyCreditCap CHECK (MonthlyCreditCap >= 0)
);
GO
