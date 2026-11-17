CREATE TABLE dbo.TenantMembers (
    TenantId INT NOT NULL,
    UserId INT NOT NULL,

    -- Suspended by a tenant administrator: the person keeps their account and every other tenant,
    -- but holds no permission here and cannot see or enter this tenant's workspaces. The tenant
    -- administrator's tool; only staff disable the account itself (User_StaffSetActive), everywhere.
    IsSuspended BIT NOT NULL CONSTRAINT DF_TenantMembers_IsSuspended DEFAULT 0,

    CONSTRAINT PK_TenantMembers PRIMARY KEY CLUSTERED (TenantId ASC, UserId ASC),
    CONSTRAINT FK_TenantMembers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TenantMembers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
GO
