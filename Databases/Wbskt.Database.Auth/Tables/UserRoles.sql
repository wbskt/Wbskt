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

-- The unique key leads with the user, which is what permission resolution reads by. Deleting a
-- workspace or a role, and the foreign key checks those deletes make, look rows up by the other
-- column: without these they scan the table under update locks, and two workspace deletes at once
-- deadlock on each other's rows.
CREATE INDEX IX_UserRoles_WorkspaceId ON dbo.UserRoles (WorkspaceId);
GO
CREATE INDEX IX_UserRoles_RoleId ON dbo.UserRoles (RoleId);
GO
