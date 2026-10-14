-- One row per workspace deleted from dbo.Workspaces, written by dbo.Workspace_Delete in the delete's
-- own transaction. dbo.Workspaces has no soft delete and an outbox row is gone once it is sent, so
-- without this nothing here remembers that a workspace existed. The management host's deleted
-- workspace sweep reads it (dbo.Workspace_GetDeletedBetween) to retire anything the
-- WorkspaceDeletedEvent did not: an event lost before the outbox existed, or one whose handler ran
-- out of retries. A row is an id and a time, so the table is never pruned.
CREATE TABLE dbo.DeletedWorkspaces (
    WorkspaceId INT NOT NULL,
    DeletedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DeletedWorkspaces_DeletedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_DeletedWorkspaces PRIMARY KEY CLUSTERED (WorkspaceId ASC)
);
GO

CREATE NONCLUSTERED INDEX IX_DeletedWorkspaces_DeletedAt ON dbo.DeletedWorkspaces (DeletedAt);
GO
