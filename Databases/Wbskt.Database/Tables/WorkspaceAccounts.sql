-- The plan each workspace's tenant is on, copied from the auth database (dbo.Tenants), which owns it.
-- The hosts that enforce plan limits on this database read it here instead of calling the auth host.
-- The management host keeps it: on TenantPlanChangedEvent, on WorkspaceCreatedEvent and in a periodic
-- full sync that repairs anything an event missed. A workspace with no row is treated as Free.
--
-- PlanRevision is the tenant's revision the copy was taken at (dbo.Tenants.PlanRevision); a copy is
-- only replaced by one at the same or a later revision (dbo.WorkspaceAccount_Upsert), so a slow sync
-- cannot put back a plan a newer change replaced. LimitOverrides is a tenant's negotiated limits as
-- JSON (PlanLimitOverrides). TenantId lets the limits pooled across a tenant's workspaces be counted
-- here.
CREATE TABLE dbo.WorkspaceAccounts
(
    WorkspaceId    INT              NOT NULL,
    TenantId       INT              NOT NULL,
    TenantRefId    UNIQUEIDENTIFIER NOT NULL,
    PlanCode       NVARCHAR(20)     NOT NULL,
    LimitOverrides NVARCHAR(MAX)    NULL,
    PlanRevision   INT              NOT NULL,
    UpdatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_WorkspaceAccounts_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_WorkspaceAccounts_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceAccounts PRIMARY KEY CLUSTERED (WorkspaceId),
    CONSTRAINT CK_WorkspaceAccounts_PlanCode CHECK (PlanCode = N'Free' OR PlanCode = N'Maker' OR PlanCode = N'Pro' OR PlanCode = N'Business')
);
GO

CREATE NONCLUSTERED INDEX IX_WorkspaceAccounts_TenantId
    ON dbo.WorkspaceAccounts (TenantId);
GO
