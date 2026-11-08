-- A workflow's unpublished changes, at most one per workflow. A draft of a workflow that has never been
-- published is the only row that workflow has until its first publish. Revision goes up on every save
-- so two editors cannot overwrite each other unseen; BaseVersion is the version the draft was started
-- from (null for a new workflow), so publishing can tell when the live version has moved on.
CREATE TABLE dbo.WorkflowDrafts
(
    RefId          UNIQUEIDENTIFIER NOT NULL,
    WorkspaceId    INT              NOT NULL,
    BaseVersion    INT              NULL,
    Revision       INT              NOT NULL,
    Name           NVARCHAR(200)    NOT NULL,
    Description    NVARCHAR(2000)   NULL,
    DefinitionJson NVARCHAR(MAX)    NOT NULL,
    CreatedBy      INT              NOT NULL,
    CreatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_WorkflowDrafts_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy      INT              NOT NULL,
    UpdatedByRefId UNIQUEIDENTIFIER NULL,
    UpdatedAt      DATETIME2(3)     NOT NULL CONSTRAINT DF_WorkflowDrafts_UpdatedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT PK_WorkflowDrafts PRIMARY KEY (RefId)
);
GO

CREATE INDEX IX_WorkflowDrafts_WorkspaceId
    ON dbo.WorkflowDrafts (WorkspaceId);
GO
