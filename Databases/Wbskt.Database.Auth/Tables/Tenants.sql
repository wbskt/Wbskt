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
    -- Who set the plan (Wbskt.Models.Billing.PlanSource): Default (never changed), Subscription (the
    -- payment provider's sync) or Operator. The sync only moves a plan it set itself or the default, so an
    -- operator's negotiated or goodwill plan is not undone by the next renewal.
    PlanSource NVARCHAR(20) NOT NULL CONSTRAINT DF_Tenants_PlanSource DEFAULT N'Default',
    -- When an operator's plan ends; the review worker then hands the tenant back to its subscription, or
    -- to Free. NULL while it has no end, and for every other source.
    PlanEndsAt DATETIME2(3) NULL,
    CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_Tenants_RefId UNIQUE (RefId),
    CONSTRAINT CK_Tenants_PlanCode CHECK (PlanCode = N'Free' OR PlanCode = N'Maker' OR PlanCode = N'Pro' OR PlanCode = N'Team' OR PlanCode = N'Business'),
    CONSTRAINT CK_Tenants_PlanSource CHECK (PlanSource = N'Default' OR PlanSource = N'Subscription' OR PlanSource = N'Operator')
);
GO
