CREATE TABLE dbo.WorkspaceMembers (
    WorkspaceId INT NOT NULL,
    UserId INT NOT NULL,
    CONSTRAINT PK_WorkspaceMembers PRIMARY KEY CLUSTERED (WorkspaceId ASC, UserId ASC),
    CONSTRAINT FK_WorkspaceMembers_Workspaces FOREIGN KEY (WorkspaceId) REFERENCES dbo.Workspaces(Id) ON DELETE CASCADE,
    CONSTRAINT FK_WorkspaceMembers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
GO

-- A user's workspaces: their workspace list, and the audit log's fan-out of their sign-ins.
CREATE INDEX IX_WorkspaceMembers_UserId ON dbo.WorkspaceMembers (UserId);
GO
