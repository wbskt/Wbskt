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
    CONSTRAINT UQ_Runs_RefId UNIQUE (RefId),
    CONSTRAINT FK_Runs_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

CREATE INDEX IX_Runs_WorkflowRefId_CorrelationKey_Running
    ON dbo.Runs (WorkflowRefId, CorrelationKey)
    WHERE Status = N'Running';
GO

CREATE INDEX IX_Runs_WorkflowRefId_CorrelationKey_Failing
    ON dbo.Runs (WorkflowRefId, CorrelationKey)
    WHERE Status = N'Failing';
GO

CREATE INDEX IX_Runs_WorkflowRefId_CorrelationKey_Cancelling
    ON dbo.Runs (WorkflowRefId, CorrelationKey)
    WHERE Status = N'Cancelling';
GO



