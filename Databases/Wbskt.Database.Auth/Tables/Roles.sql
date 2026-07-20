CREATE TABLE dbo.Roles
(
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL CONSTRAINT DF_Roles_TenantId DEFAULT (1),
    Name NVARCHAR(100) NOT NULL,
    Description NVARCHAR(255) NULL,
    CONSTRAINT FK_Roles_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT UQ_Roles_Tenant_Name UNIQUE (TenantId, Name)
)
GO
