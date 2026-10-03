CREATE TABLE dbo.UserPermissions
(
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    PermissionId INT NOT NULL,
    TenantId INT NOT NULL,
    WorkspaceId INT NULL, -- NULL = tenant-wide assignment
    IsDeny BIT NOT NULL DEFAULT 0,
    CONSTRAINT PK_UserPermissions PRIMARY KEY (Id),
    CONSTRAINT UQ_UserPermissions_Scope UNIQUE (UserId, PermissionId, TenantId, WorkspaceId),
    CONSTRAINT FK_UserPermissions_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT FK_UserPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id),
    CONSTRAINT FK_UserPermissions_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_UserPermissions_Workspace FOREIGN KEY (WorkspaceId) REFERENCES dbo.Workspaces(Id)
)
GO
