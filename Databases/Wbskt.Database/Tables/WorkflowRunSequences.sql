-- The last run number handed out per workflow (WorkflowRunSequence_Next). One row per workflow, not per
-- version, and kept apart from dbo.Runs and dbo.RunCounters so retention deleting old runs never lets a
-- number be handed out twice. No foreign key: a workflow is identified by its RefId across versions.
CREATE TABLE dbo.WorkflowRunSequences
(
    WorkflowRefId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    LastNumber    INT              NOT NULL
);
GO
