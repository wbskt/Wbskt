-- Name is deliberately not unique. Every registration without an invitation creates a tenant named
-- after the user, so a global uniqueness constraint would make sign-up fail on a name the user
-- never chose and cannot see. A tenant is identified by RefId; Name is a display label.
CREATE TABLE dbo.Tenants (
    Id INT IDENTITY(1, 1) NOT NULL,
    RefId UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    -- The plan the tenant pays for (Wbskt.Models.Billing.PlanCode). Every tenant starts on Free.
    PlanCode NVARCHAR(20) NOT NULL CONSTRAINT DF_Tenants_PlanCode DEFAULT N'Free',
    -- A negotiated contract's limits as JSON (PlanLimitOverrides), over the plan's own; usually NULL.
    PlanLimitOverrides NVARCHAR(MAX) NULL,
    -- Raised by every plan change (dbo.TenantPlan_Set). The main database's copies carry it, so an
    -- older copy never replaces a newer one, and a change made from a stale read is refused.
    PlanRevision INT NOT NULL CONSTRAINT DF_Tenants_PlanRevision DEFAULT 0,
    PlanChangedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_Tenants_RefId UNIQUE (RefId),
    CONSTRAINT CK_Tenants_PlanCode CHECK (PlanCode = N'Free' OR PlanCode = N'Maker' OR PlanCode = N'Pro' OR PlanCode = N'Business')
);
GO
