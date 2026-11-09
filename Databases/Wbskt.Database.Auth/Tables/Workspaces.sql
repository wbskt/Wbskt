CREATE TABLE dbo.Workspaces (
    Id INT IDENTITY(1, 1) NOT NULL,
    RefId UNIQUEIDENTIFIER NOT NULL,
    TenantId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(500) NULL,
    OwnerUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Workspaces PRIMARY KEY CLUSTERED (Id ASC),
    CONSTRAINT UQ_Workspaces_RefId UNIQUE (RefId),
    CONSTRAINT FK_Workspaces_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_Workspaces_Users FOREIGN KEY (OwnerUserId) REFERENCES dbo.Users(Id)
);
GO

-- A tenant's workspaces: counted against its plan (Workspace_Create) and listed with its plan
-- (WorkspaceAccount_GetPage).
CREATE NONCLUSTERED INDEX IX_Workspaces_TenantId
    ON dbo.Workspaces (TenantId);
GO
