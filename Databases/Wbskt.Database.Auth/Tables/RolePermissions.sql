CREATE TABLE [dbo].[RolePermissions]
(
    [RoleId] INT NOT NULL,
    [PermissionId] INT NOT NULL,
    [IsDeny] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
    CONSTRAINT [FK_RolePermissions_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_RolePermissions_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id])
)
