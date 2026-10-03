CREATE TABLE dbo.ScheduledFires
(
    Id                   INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    TriggerNodeId        UNIQUEIDENTIFIER NOT NULL,
    WorkflowDefinitionId INT              NOT NULL,
    WorkflowRefId        UNIQUEIDENTIFIER NOT NULL,
    CronOrInterval       NVARCHAR(200)    NOT NULL,
    NextFireAt           DATETIME2(3)     NOT NULL,
    LeasedUntil          DATETIME2(3)     NULL,
    CreatedAt            DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ScheduledFires_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id),
    -- One schedule per trigger node per definition version. Without it, two registrations of the
    -- same version (a retried or concurrent reinstate) left two rows ticking, so the workflow fired twice.
    CONSTRAINT UQ_ScheduledFires_WorkflowDefinitionId_TriggerNodeId
        UNIQUE (WorkflowDefinitionId, TriggerNodeId)
);
GO

CREATE INDEX IX_ScheduledFires_NextFireAt
    ON dbo.ScheduledFires (NextFireAt);
GO
