CREATE TABLE [dbo].[GroupRoles]
(
    [GroupId] INT NOT NULL,
    [RoleId] INT NOT NULL,
    CONSTRAINT [PK_GroupRoles] PRIMARY KEY ([GroupId], [RoleId]),
    CONSTRAINT [FK_GroupRoles_Group] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[Groups]([Id]),
    CONSTRAINT [FK_GroupRoles_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id])
)
