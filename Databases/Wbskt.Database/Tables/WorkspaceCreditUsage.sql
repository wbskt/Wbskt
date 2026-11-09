-- The workflow credits each workspace spent, per day (UTC): the ledger a plan's monthly allowance is
-- counted against (dbo.Run_Start) and the usage API reads. Only dbo.RunCredits_Post and
-- dbo.Run_DeleteRetired write it, each moving what a run consumed since it last posted
-- (RunCounters.PostedCredits), so the rows add up to exactly what the runs consumed. Kept after the
-- runs themselves are deleted.
CREATE TABLE dbo.WorkspaceCreditUsage
(
    WorkspaceId INT           NOT NULL,
    Day         DATE          NOT NULL,
    Credits     DECIMAL(18,4) NOT NULL,
    UpdatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditUsage_UpdatedAt DEFAULT SYSUTCDATETIME(),
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_WorkspaceCreditUsage_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_WorkspaceCreditUsage PRIMARY KEY (WorkspaceId, Day)
);
GO
