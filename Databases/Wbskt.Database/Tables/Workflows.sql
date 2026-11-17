-- One row per published workflow: its lifecycle state and the version that is live. A workflow that
-- exists only as a draft has no row until its first publish. The version rows in
-- dbo.WorkflowDefinitions are immutable snapshots; which of them is live is LiveDefinitionId, never
-- a flag on the version, so exactly one version is live while the workflow is Enabled and none is
-- otherwise.
--
-- State: 1 Enabled (LiveDefinitionId starts runs), 2 Disabled (editable, nothing starts),
-- 3 Deprecated (read-only), 4 Deleted (gone from every list and read; runs stay readable, and the
-- RefId can never be published again).
--
-- Every procedure that changes a workflow takes this row with UPDLOCK, HOLDLOCK after the draft's
-- row and before the version rows, and Run_Start reads it under a shared lock before anything else.
CREATE TABLE dbo.Workflows
(
    RefId            UNIQUEIDENTIFIER NOT NULL,
    WorkspaceId      INT              NOT NULL,
    State            TINYINT          NOT NULL,
    LiveDefinitionId INT              NULL,
    StateChangedBy   INT              NOT NULL,
    StateChangedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Workflows_StateChangedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt        DATETIME2(3)     NOT NULL CONSTRAINT DF_Workflows_CreatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_Workflows PRIMARY KEY (RefId),
    CONSTRAINT CK_Workflows_State CHECK (State >= 1 AND State <= 4),
    CONSTRAINT CK_Workflows_LiveOnlyWhenEnabled CHECK ((State = 1 AND LiveDefinitionId IS NOT NULL) OR (State <> 1 AND LiveDefinitionId IS NULL)),
    CONSTRAINT FK_Workflows_LiveDefinition
        FOREIGN KEY (LiveDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

-- The workspace's list, its workflow ceiling and Workspace_HasLiveContent read only the workflows
-- that still exist.
CREATE INDEX IX_Workflows_WorkspaceId
    ON dbo.Workflows (WorkspaceId, State)
    WHERE State IN (1, 2, 3);
GO
