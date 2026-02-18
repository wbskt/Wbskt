/*
Post-Deployment Script Template                   
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.      
--------------------------------------------------------------------------------------
*/

-- Roles
IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = 'Admin')
BEGIN
    INSERT INTO [dbo].[Roles] ([Name], [Description]) VALUES ('Admin', 'Administrator with full access');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = 'User')
BEGIN
    INSERT INTO [dbo].[Roles] ([Name], [Description]) VALUES ('User', 'Standard user');
END
GO

-- Permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Slug] = 'users.manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Slug], [Description]) VALUES ('users.manage', 'Manage users');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Slug] = 'roles.manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Slug], [Description]) VALUES ('roles.manage', 'Manage roles and permissions');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Slug] = 'policies.manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Slug], [Description]) VALUES ('policies.manage', 'Manage registration policies');
END
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Slug] = 'clients.manage')
BEGIN
    INSERT INTO [dbo].[Permissions] ([Slug], [Description]) VALUES ('clients.manage', 'Manage clients');
END
GO

-- RolePermissions (Admin gets all)
IF EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Name] = 'Admin')
BEGIN

    -- Variables must be declared and used within the same batch (before the GO)
    DECLARE @AdminRoleId INT = (SELECT [Id] FROM [dbo].[Roles] WHERE [Name] = 'Admin');

    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [IsDeny])
    SELECT @AdminRoleId, p.[Id], 0
    FROM [dbo].[Permissions] p
    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] rp WHERE rp.[RoleId] = @AdminRoleId AND rp.[PermissionId] = p.[Id]);
END
GO

-- Users (Root with enforced ID)
SET IDENTITY_INSERT [dbo].[Users] ON;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = 1)
BEGIN
    -- Password is 'Password123!'
    INSERT INTO [dbo].[Users] ([Id], [Username], [Email], [PasswordHash], [IsActive])
    VALUES (1, 'root', 'admin@wbskt.com', 'AQAAAAIAAYagAAAAEFpR/CYNxe2N5aUZv3U+eodymZIb6BfMSJKxovngFs/yXsN7ozQu/K2ajy8olqDZLQ==', 1);
END
GO

SET IDENTITY_INSERT [dbo].[Users] OFF;
GO

-- UserRoles (Root is Admin)
IF EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = 1)
BEGIN
    DECLARE @AdminRoleId_UR INT = (SELECT [Id] FROM [dbo].[Roles] WHERE [Name] = 'Admin');
    IF @AdminRoleId_UR IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = 1 AND [RoleId] = @AdminRoleId_UR)
    BEGIN
        INSERT INTO [dbo].[UserRoles] ([UserId], [RoleId]) VALUES (1, @AdminRoleId_UR);
    END
END
GO

-- Default Workspace (with enforced ID)
SET IDENTITY_INSERT [dbo].[Workspaces] ON;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[Workspaces] WHERE [Id] = 1)
BEGIN
    INSERT INTO [dbo].[Workspaces] ([Id], [RefId], [Name], [Description], [OwnerUserId])
    VALUES (1, NEWID(), 'Default Workspace', 'Default workspace for the primary administrator.', 1);
END
GO

SET IDENTITY_INSERT [dbo].[Workspaces] OFF;
GO

-- Default Workspace Member
IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkspaceMembers] WHERE [WorkspaceId] = 1 AND [UserId] = 1)
BEGIN
    INSERT INTO [dbo].[WorkspaceMembers] ([WorkspaceId], [UserId], [Role])
    VALUES (1, 1, 2); -- Role 2 = Admin
END
GO