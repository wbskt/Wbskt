CREATE TABLE dbo.UserGroups
(
    UserId INT NOT NULL,
    GroupId INT NOT NULL,
    CONSTRAINT PK_UserGroups PRIMARY KEY (UserId, GroupId),
    CONSTRAINT FK_UserGroups_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
    CONSTRAINT FK_UserGroups_Group FOREIGN KEY (GroupId) REFERENCES dbo.Groups(Id)
)
GO
