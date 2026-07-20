/*
One-time pre-publish helper for EXISTING Auth databases upgrading to the tenant-aware schema.

Why: the upgrade adds Workspaces.TenantId (NOT NULL, DEFAULT 1) with an FK to dbo.Tenants.
SqlPackage validates that FK during schema deployment - BEFORE the post-deployment script
seeds the default tenant - so publishing against a database that already has workspace
rows fails unless dbo.Tenants with Id = 1 exists first.

Usage: run this script manually against the Auth DB once, BEFORE `sqlpackage /Action:Publish`
(or before Deploy-Databases.ps1 without -Fresh). Fresh/empty databases do NOT need it.
This file lives outside the sqlproj directory on purpose - the DACPAC build sweeps up
any .sql file inside the project folder into the model.
*/

IF OBJECT_ID('dbo.Tenants') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants (
        Id INT IDENTITY(1, 1) NOT NULL,
        RefId UNIQUEIDENTIFIER NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Description NVARCHAR(500) NULL,
        CreatedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_Tenants PRIMARY KEY CLUSTERED (Id ASC),
        CONSTRAINT UQ_Tenants_RefId UNIQUE (RefId),
        CONSTRAINT UQ_Tenants_Name UNIQUE (Name)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 1)
BEGIN
    SET IDENTITY_INSERT dbo.Tenants ON;

    INSERT INTO dbo.Tenants (Id, RefId, Name, Description)
    VALUES (1, NEWID(), 'Default Tenant', 'Default tenant for the primary administrator.');

    SET IDENTITY_INSERT dbo.Tenants OFF;
END
GO
