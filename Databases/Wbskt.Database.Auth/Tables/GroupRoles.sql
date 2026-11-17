CREATE TABLE dbo.GroupRoles (
    Id INT IDENTITY(1,1) NOT NULL,
    GroupId INT NOT NULL,
    RoleId  INT NOT NULL,
    TenantId INT NOT NULL,
    WorkspaceId INT NULL, -- NULL = tenant-wide assignment

    -- Constraints
    CONSTRAINT PK_GroupRoles           PRIMARY KEY (Id),
    CONSTRAINT UQ_GroupRoles_Scope     UNIQUE (GroupId, RoleId, TenantId, WorkspaceId),
    CONSTRAINT FK_GroupRoles_Groups    FOREIGN KEY (GroupId) REFERENCES dbo.Groups (Id),
    CONSTRAINT FK_GroupRoles_Roles     FOREIGN KEY (RoleId)  REFERENCES dbo.Roles (Id),
    CONSTRAINT FK_GroupRoles_Tenants   FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id),
    CONSTRAINT FK_GroupRoles_Workspace FOREIGN KEY (WorkspaceId) REFERENCES dbo.Workspaces (Id)
);
GO

-- For the deletes by workspace and by role and their foreign key checks, as on dbo.UserRoles.
CREATE INDEX IX_GroupRoles_WorkspaceId ON dbo.GroupRoles (WorkspaceId);
GO
CREATE INDEX IX_GroupRoles_RoleId ON dbo.GroupRoles (RoleId);
GO
