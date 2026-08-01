/*
One-time pre-publish helper for EXISTING Auth databases gaining public references on roles
and groups.

Why: the management API previously exposed raw identity ints for roles and groups. Every other
entity in the system is addressed by an opaque RefId (see "The ID Boundary" in
Docs/Coding.Conventions.md), so both tables gain RefId UNIQUEIDENTIFIER NOT NULL with a unique
constraint. Adding a NOT NULL column plus a UNIQUE constraint in one publish is the kind of
change SqlPackage will refuse, or will attempt as a table rebuild, against a table that already
has rows. Doing it here in three explicit steps keeps the publish itself a no-op.

Each step is idempotent, so re-running the script is safe.

Usage: run this manually against the Auth DB once, BEFORE `sqlpackage /Action:Publish`
(or before Deploy-Databases.ps1 without -Fresh). Fresh/empty databases do NOT need it.
This file lives outside the sqlproj directory on purpose - the DACPAC build sweeps up any
.sql file inside the project folder into the model.
*/

-- dbo.Roles
IF OBJECT_ID('dbo.Roles') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Roles') AND name = 'RefId')
BEGIN
    -- 1. Add nullable so existing rows are accepted.
    ALTER TABLE dbo.Roles ADD RefId UNIQUEIDENTIFIER NULL;
END
GO

-- 2. Backfill. A set-based UPDATE with NEWID() does produce a distinct value per row.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Roles') AND name = 'RefId')
BEGIN
    UPDATE dbo.Roles SET RefId = NEWID() WHERE RefId IS NULL;
END
GO

-- 3. Tighten to NOT NULL and add the default and unique constraint the DACPAC expects.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Roles') AND name = 'RefId' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.Roles ALTER COLUMN RefId UNIQUEIDENTIFIER NOT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Roles_RefId')
BEGIN
    ALTER TABLE dbo.Roles ADD CONSTRAINT DF_Roles_RefId DEFAULT NEWID() FOR RefId;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Roles_RefId')
BEGIN
    ALTER TABLE dbo.Roles ADD CONSTRAINT UQ_Roles_RefId UNIQUE (RefId);
END
GO

-- dbo.Groups
IF OBJECT_ID('dbo.Groups') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Groups') AND name = 'RefId')
BEGIN
    ALTER TABLE dbo.Groups ADD RefId UNIQUEIDENTIFIER NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Groups') AND name = 'RefId')
BEGIN
    UPDATE dbo.Groups SET RefId = NEWID() WHERE RefId IS NULL;
END
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Groups') AND name = 'RefId' AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.Groups ALTER COLUMN RefId UNIQUEIDENTIFIER NOT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Groups_RefId')
BEGIN
    ALTER TABLE dbo.Groups ADD CONSTRAINT DF_Groups_RefId DEFAULT NEWID() FOR RefId;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Groups_RefId')
BEGIN
    ALTER TABLE dbo.Groups ADD CONSTRAINT UQ_Groups_RefId UNIQUE (RefId);
END
GO
