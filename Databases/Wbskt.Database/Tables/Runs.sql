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
    CONSTRAINT UQ_Runs_RefId UNIQUE (RefId),
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
    INCLUDE (Status);
GO

-- One run per inbound event per trigger registration. The dispatcher's idempotency claims stop a
-- retried event from starting a registration twice while the claims settle cleanly; this is what
-- still holds when a run was created and the attempt failed before its claim was marked done.
CREATE UNIQUE INDEX UX_Runs_TriggerStartKey
    ON dbo.Runs (TriggerStartKey)
    WHERE TriggerStartKey IS NOT NULL;
GO

-- Workspace-wide run listing joins Runs to WorkflowDefinitions on this column. SQL Server does not
-- index foreign keys automatically, so without this the join scans.
CREATE INDEX IX_Runs_WorkflowDefinitionId_Id
    ON dbo.Runs (WorkflowDefinitionId, Id DESC)
    INCLUDE (Status);
GO

-- Run_Start's per-workspace ceiling counts the active runs while it holds the workspace's applock;
-- filtered like IX_Runs_WorkflowRefId_CorrelationKey_Active so the count never reads retained runs.
CREATE INDEX IX_Runs_WorkflowDefinitionId_Active
    ON dbo.Runs (WorkflowDefinitionId)
    WHERE Status IN (N'Running', N'Failing', N'Cancelling');
GO

-- Drives both retention sweeps (HistoryEvent_DeleteForRetiredRuns, Run_DeleteRetired).
CREATE INDEX IX_Runs_CompletedAt
    ON dbo.Runs (CompletedAt)
    WHERE CompletedAt IS NOT NULL;
GO
