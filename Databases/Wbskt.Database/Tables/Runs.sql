CREATE TABLE dbo.Runs
(
    Id                       INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    RefId                    UNIQUEIDENTIFIER NOT NULL,
    WorkflowDefinitionId     INT              NOT NULL,
    WorkflowRefId            UNIQUEIDENTIFIER NOT NULL,
    WorkflowVersion          INT              NOT NULL,
    TriggerNodeId            UNIQUEIDENTIFIER NOT NULL,
    CorrelationKey           NVARCHAR(400)    NULL,
    Status                   NVARCHAR(32)     NOT NULL DEFAULT N'Running',
    StartedAt                DATETIME2(3)     NOT NULL,
    CompletedAt              DATETIME2(3)     NULL,
    CancellationRequestedAt  DATETIME2(3)     NULL,
    CancellationReason       NVARCHAR(500)    NULL,
    CreditBudget             DECIMAL(18,4)    NOT NULL,
    CreatedAt                DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    -- Which inbound event and trigger registration started this run (Run_Start). Null for a run
    -- started any other way.
    TriggerStartKey          NVARCHAR(200)    NULL,
    -- The run whose sub-workflow step started this one (Run_Start). Null for a run started any other
    -- way. Cancelling or failing the parent cancels the children it names, so a child is not left
    -- running for a parent that will never read its result.
    ParentRunRefId           UNIQUEIDENTIFIER NULL,
    -- What the run's control:end node handed back (Run_SetOutput), delivered to a parent's
    -- sub-workflow step as childResult.output. Null when no End node set one.
    OutputJson               NVARCHAR(MAX)    NULL,
    -- The run's number within its workflow (#1, #2, ...), from WorkflowRunSequence_Next. The default
    -- exists only so the column could be added to a table with rows: runs made before numbering read 0
    -- and are not renumbered (the product had not been released).
    Number                   INT              NOT NULL CONSTRAINT DF_Runs_Number DEFAULT 0,
    -- Started by hand from the workflow page's Test run, against a test copy of a draft or the live
    -- version. Shares the workflow's numbering; left out of stats.
    IsTest                   BIT              NOT NULL CONSTRAINT DF_Runs_IsTest DEFAULT 0,
    -- The workspace the run's definition belongs to, copied by Run_Start and never changed, so a
    -- workspace's runs are read without going through dbo.WorkflowDefinitions. Null only for a run
    -- started before the column existed, which the post-deployment script fills in, until it is made
    -- NOT NULL in the release after (#1059).
    WorkspaceId              INT              NULL,
    CONSTRAINT UQ_Runs_RefId UNIQUE (RefId),
    -- The words RunStatus (Wbskt.Workflow.Abstraction) defines; a change there is a change here.
    -- Written out with OR, not IN: SQL Server stores IN expanded, and the DACPAC would never match it.
    CONSTRAINT CK_Runs_Status CHECK (Status = N'Running' OR Status = N'Failing' OR Status = N'Cancelling'
        OR Status = N'Succeeded' OR Status = N'Failed' OR Status = N'PartiallyFailed'
        OR Status = N'Cancelled' OR Status = N'OutOfCredits'),
    CONSTRAINT FK_Runs_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

-- Covers Run_GetActiveBy_Correlation, which filters on all three columns. TriggerNodeId is part of
-- the key because two triggers in one workflow can share a correlation key.
CREATE INDEX IX_Runs_WorkflowRefId_CorrelationKey_Active
    ON dbo.Runs (WorkflowRefId, TriggerNodeId, CorrelationKey)
    WHERE Status IN (N'Running', N'Failing', N'Cancelling');
GO

-- The run-history page: Run_ListBy_WorkflowRefId pages backwards by Id, optionally filtered by
-- status. Unlike the filtered index above this must cover terminal runs too - they are the bulk of
-- what the page shows, and without this the query scans a table that grows with every run ever made.
CREATE INDEX IX_Runs_WorkflowRefId_Id
    ON dbo.Runs (WorkflowRefId, Id DESC)
    INCLUDE (Status, IsTest);
GO

-- One run per inbound event per trigger registration. The dispatcher's idempotency claims stop a
-- retried event from starting a registration twice while the claims settle cleanly; this is what
-- still holds when a run was created and the attempt failed before its claim was marked done.
CREATE UNIQUE INDEX UX_Runs_TriggerStartKey
    ON dbo.Runs (TriggerStartKey)
    WHERE TriggerStartKey IS NOT NULL;
GO

-- A workflow's run numbers are unique; filtered so the runs made before numbering, all 0, do not collide.
CREATE UNIQUE INDEX UX_Runs_WorkflowRefId_Number
    ON dbo.Runs (WorkflowRefId, Number)
    WHERE Number > 0;
GO

-- The workspace's run list (Run_ListBy_WorkspaceId) pages backwards by Id, optionally by status, and
-- the workspace-wide existence checks (purge, retire) seek its first row.
CREATE INDEX IX_Runs_WorkspaceId_Id
    ON dbo.Runs (WorkspaceId, Id DESC)
    INCLUDE (Status, IsTest);
GO

-- Run_Start's per-workspace ceiling counts the active runs while it holds the workspace's applock;
-- filtered like IX_Runs_WorkflowRefId_CorrelationKey_Active so the count never reads retained runs.
-- CreditBudget is included for the budgets the runs going hold (dbo.RunCreditsHeld), summed on every
-- start and lease extension.
CREATE INDEX IX_Runs_WorkspaceId_Active
    ON dbo.Runs (WorkspaceId)
    INCLUDE (CreditBudget)
    WHERE Status IN (N'Running', N'Failing', N'Cancelling');
GO

-- A workflow version's runs, for the checks that a definition is still referenced before it is deleted.
CREATE INDEX IX_Runs_WorkflowDefinitionId
    ON dbo.Runs (WorkflowDefinitionId);
GO

-- Run_DeleteRetired seeks each workflow's finished runs up to its workspace's own cutoff, so the runs a
-- longer plan keeps are never read by a sweep that cannot delete them.
CREATE INDEX IX_Runs_WorkflowDefinitionId_CompletedAt
    ON dbo.Runs (WorkflowDefinitionId, CompletedAt)
    WHERE CompletedAt IS NOT NULL;
GO

-- Run_GetActiveChildrenBy_ParentRefId: the cancel cascade reads the children still going under a
-- parent. Filtered to children that are active, so it never reads the retained ones.
CREATE INDEX IX_Runs_ParentRunRefId_Active
    ON dbo.Runs (ParentRunRefId)
    WHERE ParentRunRefId IS NOT NULL AND Status IN (N'Running', N'Failing', N'Cancelling');
GO

-- The stats procedures (Run_GetStatsBy_WorkflowRefId, Run_GetStatsBy_WorkspaceId,
-- Run_GetStatsPerWorkflowBy_WorkspaceId) read a time window of runs by CreatedAt. Without these they
-- scan every run a workflow or workspace ever made.
CREATE INDEX IX_Runs_WorkflowRefId_CreatedAt
    ON dbo.Runs (WorkflowRefId, CreatedAt)
    INCLUDE (Status, StartedAt, CompletedAt, IsTest);
GO

-- WorkflowRefId is carried for the per-workflow breakdown (dbo.RunsInWindow), so it needs no key lookup per run.
CREATE INDEX IX_Runs_WorkspaceId_CreatedAt
    ON dbo.Runs (WorkspaceId, CreatedAt)
    INCLUDE (Status, StartedAt, CompletedAt, IsTest, WorkflowRefId);
GO

-- Run_Start's one-test-run-at-a-time check, and finding the test run that is going.
CREATE INDEX IX_Runs_WorkflowRefId_Test_Active
    ON dbo.Runs (WorkflowRefId)
    WHERE IsTest = 1 AND Status IN (N'Running', N'Failing', N'Cancelling');
GO
