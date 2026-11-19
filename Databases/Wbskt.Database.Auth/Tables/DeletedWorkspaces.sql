-- One row per workspace deleted from dbo.Workspaces, written by dbo.Workspace_Delete in the delete's
-- own transaction. dbo.Workspaces has no soft delete and an outbox row is gone once it is sent, so
-- without this nothing here remembers that a workspace existed. The management host's deleted
-- workspace sweep reads it (dbo.Workspace_GetDeletedBetween) to retire anything the
-- WorkspaceDeletedEvent did not: an event lost before the outbox existed, or one whose handler ran
-- out of retries. It is also the tombstone admin.Workspaces lists a deleted workspace from, so it
-- keeps the workspace's reference, tenant, name, owner and creation time. Those are null on the rows
-- backfilled from before it kept them. Deleting an account clears the names of its workspaces here, as
-- it clears its tenant's (dbo.User_Delete). A row is small, so the table is never pruned.
CREATE TABLE dbo.DeletedWorkspaces (
    WorkspaceId INT NOT NULL,
    DeletedAt DATETIME2(3) NOT NULL CONSTRAINT DF_DeletedWorkspaces_DeletedAt DEFAULT SYSUTCDATETIME(),
    RefId UNIQUEIDENTIFIER NULL,
    TenantId INT NULL,
    Name NVARCHAR(100) NULL,
    OwnerUserId INT NULL,
    CreatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_DeletedWorkspaces PRIMARY KEY CLUSTERED (WorkspaceId ASC)
);
GO

CREATE NONCLUSTERED INDEX IX_DeletedWorkspaces_DeletedAt ON dbo.DeletedWorkspaces (DeletedAt);
GO
