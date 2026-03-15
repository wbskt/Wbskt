/*
Post-Deployment Script Template                   
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.      
--------------------------------------------------------------------------------------
*/

-- Roles
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Admin')
BEGIN
    INSERT INTO dbo.Roles (Name, Description) VALUES ('Admin', 'Administrator with full access');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'User')
BEGIN
    INSERT INTO dbo.Roles (Name, Description) VALUES ('User', 'Standard user');
END
GO

-- Permissions
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Slug = 'users.manage')
BEGIN
    INSERT INTO dbo.Permissions (Slug, Description) VALUES ('users.manage', 'Manage users');
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

-- RolePermissions (Admin gets ALL)
IF EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Admin')
BEGIN

    -- Variables must be declared and used within the same batch (before the GO)
    DECLARE @AdminRoleId INT = (SELECT Id FROM dbo.Roles WHERE Name = 'Admin');

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, IsDeny)
    SELECT @AdminRoleId, p.Id, 0
    FROM dbo.Permissions p
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = @AdminRoleId AND rp.PermissionId = p.Id);
END
GO

-- Users (Root WITH enforced ID)
SET IDENTITY_INSERT dbo.Users ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
BEGIN
    -- Password is 'Password123!'
    INSERT INTO dbo.Users (Id, Username, Email, PasswordHash, IsActive)
    VALUES (1, 'root', 'admin@wbskt.com', 'AQAAAAIAAYagAAAAEFpR/CYNxe2N5aUZv3U+eodymZIb6BfMSJKxovngFs/yXsN7ozQu/K2ajy8olqDZLQ==', 1);
END
GO

SET IDENTITY_INSERT dbo.Users OFF;
GO

-- UserRoles (Root is Admin)
IF EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
BEGIN
    DECLARE @AdminRoleId_UR INT = (SELECT Id FROM dbo.Roles WHERE Name = 'Admin');
    IF @AdminRoleId_UR IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles WHERE UserId = 1 AND RoleId = @AdminRoleId_UR)
    BEGIN
        INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (1, @AdminRoleId_UR);
    END
END
GO

-- DEFAULT Workspace (WITH enforced ID)
SET IDENTITY_INSERT dbo.Workspaces ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Workspaces WHERE Id = 1)
BEGIN
    INSERT INTO dbo.Workspaces (Id, RefId, Name, Description, OwnerUserId)
    VALUES (1, NEWID(), 'Default Workspace', 'Default workspace for the primary administrator.', 1);
END
GO

SET IDENTITY_INSERT dbo.Workspaces OFF;
GO

-- DEFAULT Workspace Member
IF NOT EXISTS (SELECT 1 FROM dbo.WorkspaceMembers WHERE WorkspaceId = 1 AND UserId = 1)
BEGIN
    INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId, Role)
    VALUES (1, 1, 2); -- Role 2 = Admin
END
GO