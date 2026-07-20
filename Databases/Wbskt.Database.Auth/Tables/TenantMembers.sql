CREATE TABLE dbo.TenantMembers (
    TenantId INT NOT NULL,
    UserId INT NOT NULL,
    CONSTRAINT PK_TenantMembers PRIMARY KEY CLUSTERED (TenantId ASC, UserId ASC),
    CONSTRAINT FK_TenantMembers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TenantMembers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
GO
