CREATE TABLE dbo.UserRoles
(
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    RoleId INT NOT NULL,
    TenantId INT NOT NULL,
    WorkspaceId INT NULL, -- NULL = tenant-wide assignment
    CONSTRAINT PK_UserRoles PRIMARY KEY (Id),
    CONSTRAINT UQ_UserRoles_Scope UNIQUE (UserId, RoleId, TenantId, WorkspaceId),
    CONSTRAINT FK_UserRoles_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT FK_UserRoles_Role FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id),
    CONSTRAINT FK_UserRoles_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_UserRoles_Workspace FOREIGN KEY (WorkspaceId) REFERENCES dbo.Workspaces(Id)
)
GO
