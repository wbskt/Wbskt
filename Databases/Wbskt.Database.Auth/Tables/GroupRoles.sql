CREATE TABLE dbo.GroupRoles (
    GroupId INT NOT NULL,
    RoleId  INT NOT NULL,

    -- Constraints
    CONSTRAINT PK_GroupRoles        PRIMARY KEY (GroupId, RoleId),
    CONSTRAINT FK_GroupRoles_Groups FOREIGN KEY (GroupId) REFERENCES dbo.Groups (Id),
    CONSTRAINT FK_GroupRoles_Roles  FOREIGN KEY (RoleId)  REFERENCES dbo.Roles (Id)
);
GO
