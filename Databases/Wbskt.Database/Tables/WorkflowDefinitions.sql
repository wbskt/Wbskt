CREATE TABLE dbo.WorkflowDefinitions
(
    Id              INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId           UNIQUEIDENTIFIER NOT NULL,
    Version         INT              NOT NULL,
    WorkspaceId     INT              NOT NULL,
    Name            NVARCHAR(200)    NOT NULL,
    Description     NVARCHAR(2000)   NULL,
    IsEnabled       BIT              NOT NULL DEFAULT 1,
    DefinitionJson  NVARCHAR(MAX)    NOT NULL,
    PublishedBy     INT              NOT NULL,
    CreatedAt       DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    -- 'Version' is a published version. 'Test' is the frozen copy a test run was started from (a draft,
    -- or the live version at the time): the engine loads it by Id like any version, but it registers no
    -- triggers and is invisible to every version lookup, which all filter on Kind = 'Version'. Its
    -- Version is the version it was based on (0 for a workflow never published).
    Kind            NVARCHAR(16)     NOT NULL CONSTRAINT DF_WorkflowDefinitions_Kind DEFAULT N'Version',
    CONSTRAINT CK_WorkflowDefinitions_Kind CHECK (Kind = N'Version' OR Kind = N'Test')
);
GO

-- One row per published version. Test copies share their base version's number, so they are left out.
CREATE UNIQUE INDEX UX_WorkflowDefinitions_RefId_Version
    ON dbo.WorkflowDefinitions (RefId, Version)
    WHERE Kind = N'Version';
GO

-- Every lookup by RefId alone: ownership checks, WorkflowDefinition_Delete's range lock, and test copies.
-- The filtered index above serves only queries that name Kind = 'Version', so without this the rest scan.
CREATE INDEX IX_WorkflowDefinitions_RefId
    ON dbo.WorkflowDefinitions (RefId, Version)
    INCLUDE (WorkspaceId, Kind);
GO

-- Test copies by age, for retention's sweep of the ones no run points at any more.
CREATE INDEX IX_WorkflowDefinitions_Test
    ON dbo.WorkflowDefinitions (CreatedAt)
    WHERE Kind = N'Test';
GO

CREATE INDEX IX_WorkflowDefinitions_WorkspaceId_Enabled
    ON dbo.WorkflowDefinitions (WorkspaceId, IsEnabled);
GO
