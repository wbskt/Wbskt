CREATE TABLE dbo.Roles
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RefId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Roles_RefId DEFAULT NEWID(),
    TenantId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255) NULL,
    -- Which of a tenant's built-in roles this is: 'Admin' (receives every permission, on creation and
    -- on every deploy) or 'User' (the default member reads). NULL for roles an administrator made.
    -- Seeding and grants find the built-in roles by this, never by Name, so renaming one keeps it
    -- working and a custom role cannot become Admin by taking the name.
    Kind NVARCHAR(16) NULL,
    CONSTRAINT FK_Roles_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT UQ_Roles_RefId UNIQUE (RefId),
    CONSTRAINT UQ_Roles_Tenant_Name UNIQUE (TenantId, Name),
    CONSTRAINT CK_Roles_Kind CHECK (Kind IN (N'Admin', N'User'))
)
GO

CREATE UNIQUE NONCLUSTERED INDEX UQ_Roles_Tenant_Kind
    ON dbo.Roles (TenantId, Kind)
    WHERE Kind IS NOT NULL;
GO
