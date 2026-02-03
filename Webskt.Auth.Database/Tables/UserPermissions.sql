CREATE TABLE [dbo].[UserPermissions]
(
    [UserId] INT NOT NULL,
    [PermissionId] INT NOT NULL,
    [IsDeny] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [PK_UserPermissions] PRIMARY KEY ([UserId], [PermissionId]),
    CONSTRAINT [FK_UserPermissions_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserPermissions_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id])
)
