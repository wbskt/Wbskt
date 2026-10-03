/*
Post-Deployment Script Template                   
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.      
--------------------------------------------------------------------------------------
*/

-- Users.IsEmailVerified backfill: the accounts the pre-deployment script recorded, from before the
-- column existed, are marked verified, and the scratch table goes. Through EXEC because the table is
-- not part of the model.
IF OBJECT_ID('dbo.__EmailVerifiedBackfill', 'U') IS NOT NULL
BEGIN
    PRINT 'Backfilling dbo.Users.IsEmailVerified for accounts that predate it.';
    EXEC('UPDATE u SET IsEmailVerified = 1 FROM dbo.Users u JOIN dbo.__EmailVerifiedBackfill b ON b.Id = u.Id;');
    EXEC('DROP TABLE dbo.__EmailVerifiedBackfill;');
END
GO

-- RefreshTokens: the rows the pre-deployment script carried across the switch to hashed tokens go
-- back, with their original Ids, and the scratch table goes. Through EXEC because the table is not
-- part of the model. A row is skipped if its Id or hash is already present, so a re-run after a
-- partial one cannot duplicate or collide.
IF OBJECT_ID('dbo.__RefreshTokenHashBackfill', 'U') IS NOT NULL
BEGIN
    PRINT 'Restoring dbo.RefreshTokens rows with hashed tokens.';
    EXEC('
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        SET IDENTITY_INSERT dbo.RefreshTokens ON;
        INSERT INTO dbo.RefreshTokens
            (Id, UserId, TokenHash, Expires, Revoked, CreatedByIp, RevokedByIp, ReplacedByTokenHash)
        SELECT B.Id, B.UserId, B.TokenHash, B.Expires, B.Revoked, B.CreatedByIp, B.RevokedByIp, B.ReplacedByTokenHash
        FROM dbo.__RefreshTokenHashBackfill B
        WHERE NOT EXISTS (SELECT 1 FROM dbo.RefreshTokens R WHERE R.Id = B.Id OR R.TokenHash = B.TokenHash);
        SET IDENTITY_INSERT dbo.RefreshTokens OFF;
        DROP TABLE dbo.__RefreshTokenHashBackfill;
        COMMIT TRANSACTION;');
END
GO

-- DEFAULT Tenant (WITH enforced ID) - must exist before Roles/Groups/Workspaces (TenantId default = 1)
SET IDENTITY_INSERT dbo.Tenants ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 1)
BEGIN
    INSERT INTO dbo.Tenants (Id, RefId, Name, Description)
    VALUES (1, NEWID(), 'Default Tenant', 'Default tenant for the primary administrator.');
END
GO

SET IDENTITY_INSERT dbo.Tenants OFF;
GO

-- Roles
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Admin' AND TenantId = 1)
BEGIN
    INSERT INTO dbo.Roles (TenantId, Name, Description) VALUES (1, 'Admin', 'Administrator with full access');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'User' AND TenantId = 1)
BEGIN
    INSERT INTO dbo.Roles (TenantId, Name, Description) VALUES (1, 'User', 'Standard user');
END
GO

-- Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'users.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('users.read', 'Read users, groups and tenant membership');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'users.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('users.manage', 'Manage users');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'roles.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('roles.read', 'Read roles, permissions and assignments');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'roles.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('roles.manage', 'Manage roles and permissions');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'policies.read')
    BEGIN
        INSERT INTO dbo.Permissions (Slug, Description) VALUES ('policies.read', 'Read registration policies');
    END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'policies.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('policies.manage', 'Manage registration policies');
END
GO

-- Clients Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'clients.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('clients.manage', 'Manage clients');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'clients.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('clients.read', 'Read clients');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'clients.update')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('clients.update', 'Update client status');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'clients.command')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('clients.command', 'Send commands to clients');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'clients.ping')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('clients.ping', 'Ping clients');
END
GO

-- Message Template Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'templates.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('templates.read', 'Read message templates');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'templates.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('templates.manage', 'Manage message templates');
END
GO

-- Workflow Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workflows.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workflows.read', 'Read workflows');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workflows.create')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workflows.create', 'Create workflows');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workflows.update')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workflows.update', 'Update workflows');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workflows.delete')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workflows.delete', 'Delete workflows');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workflows.execute')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workflows.execute', 'Start, signal and cancel workflow runs');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'workspace.join')
    BEGIN
        INSERT INTO dbo.Permissions (Slug, Description) VALUES ('workspace.join', 'Join workspaces');
    END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'logs.read')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('logs.read', 'Read event logs');
END
GO

-- RolePermissions (every tenant's Admin gets ALL).
--
-- Set-based across all Admin roles on purpose. Roles are tenant-scoped, so once a second tenant
-- exists there is more than one row named 'Admin' and assigning them to a scalar variable fails the
-- whole post-deployment script with "Subquery returned more than 1 value". This is also the only
-- mechanism by which a permission slug added after a tenant was created reaches that tenant's
-- administrators -- Tenant_Create grants the catalogue as it stood at creation time and never
-- revisits it -- so it has to run for every tenant, not just the default one.
INSERT INTO dbo.RolePermissions (RoleId, PermissionId, IsDeny)
SELECT r.Id, p.Id, 0
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.Name = 'Admin'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
GO

-- Users (Root WITH enforced ID)
SET IDENTITY_INSERT dbo.Users ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
BEGIN
    -- Password is 'Password123!'
    -- IsEmailVerified = 1: nothing will ever mail root a verification link, and sign-in requires a
    -- verified address, so seeding it unverified would ship a database whose only administrator
    -- cannot log in.
    INSERT INTO dbo.Users (Id, Username, Email, PasswordHash, IsActive, IsEmailVerified)
    VALUES (1, 'root', 'admin@wbskt.com', 'AQAAAAIAAYagAAAAEFpR/CYNxe2N5aUZv3U+eodymZIb6BfMSJKxovngFs/yXsN7ozQu/K2ajy8olqDZLQ==', 1, 1);
END
GO

SET IDENTITY_INSERT dbo.Users OFF;
GO

-- TenantMembers (Root belongs to the default tenant)
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1) AND NOT EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = 1 AND UserId = 1)
BEGIN
    INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (1, 1);
END
GO

-- UserRoles (Root is tenant-wide Admin in the default tenant)
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
BEGIN
    DECLARE @AdminRoleId_UR INT = (SELECT Id FROM dbo.Roles WHERE TenantId = 1 AND Name = 'Admin');
    IF @AdminRoleId_UR IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = 1 AND RoleId = @AdminRoleId_UR AND TenantId = 1 AND WorkspaceId IS NULL)
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId, TenantId, WorkspaceId) VALUES (1, @AdminRoleId_UR, 1, NULL);
    END
END
GO

-- DEFAULT Workspace (WITH enforced ID)
SET IDENTITY_INSERT dbo.Workspaces ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = 1)
BEGIN
    INSERT INTO dbo.Workspaces (Id, RefId, TenantId, Name, Description, OwnerUserId)
    VALUES (1, NEWID(), 1, 'Default Workspace', 'Default workspace for the primary administrator.', 1);
END
GO

SET IDENTITY_INSERT dbo.Workspaces OFF;
GO

-- DEFAULT Workspace Member
IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = 1 AND UserId = 1)
BEGIN
    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId)
    VALUES (1, 1);
END
GO