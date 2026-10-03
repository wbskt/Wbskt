-- A deleted workflow, one row per RefId. Deleting is a tombstone rather than removing the version
-- rows: runs point at their definition by id and stay readable, as Workspace_Retire keeps them. The
-- RefId is gone from every current-version lookup and summary, and cannot be published again.
CREATE TABLE dbo.WorkflowDeletions
(
    RefId       UNIQUEIDENTIFIER NOT NULL,
    WorkspaceId INT              NOT NULL,
    DeletedBy   INT              NOT NULL,
    DeletedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_WorkflowDeletions_DeletedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_WorkflowDeletions PRIMARY KEY (RefId)
);
GO
