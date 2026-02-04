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

-- Users (Root)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'admin@wbskt.com')
BEGIN

    -- Password is 'Password123!'
    DECLARE @RootEmail NVARCHAR(100) = 'admin@wbskt.com';
    DECLARE @RootUsername NVARCHAR(50) = 'root';
    DECLARE @PasswordHash NVARCHAR(255) = 'AQAAAAIAAYagAAAAEFpR/CYNxe2N5aUZv3U+eodymZIb6BfMSJKxovngFs/yXsN7ozQu/K2ajy8olqDZLQ==';

    INSERT INTO [dbo].[Users] ([Username], [Email], [PasswordHash], [IsActive])
    VALUES (@RootUsername, @RootEmail, @PasswordHash, 1);
END
GO

-- UserRoles (Root is Admin)
IF EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Email] = 'admin@wbskt.com')
BEGIN
    DECLARE @RootUserId_UR INT = (SELECT [Id] FROM [dbo].[Users] WHERE [Email] = 'admin@wbskt.com');
    DECLARE @AdminRoleId_UR INT = (SELECT [Id] FROM [dbo].[Roles] WHERE [Name] = 'Admin');

    IF (@RootUserId_UR IS NOT NULL AND @AdminRoleId_UR IS NOT NULL)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = @RootUserId_UR AND [RoleId] = @AdminRoleId_UR)
        BEGIN
            INSERT INTO [dbo].[UserRoles] ([UserId], [RoleId]) VALUES (@RootUserId_UR, @AdminRoleId_UR);
        END
    END
END
GO