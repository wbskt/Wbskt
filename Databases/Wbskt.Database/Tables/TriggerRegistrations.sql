CREATE TABLE dbo.TriggerRegistrations
(
    Id                    INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowDefinitionId  INT              NOT NULL,
    WorkflowRefId         UNIQUEIDENTIFIER NOT NULL,
    WorkflowVersion       INT              NOT NULL,
    TriggerNodeId         UNIQUEIDENTIFIER NOT NULL,
    TriggerKind           NVARCHAR(64)     NOT NULL,
    TriggerKey            NVARCHAR(400)    NOT NULL,
    CorrelationExpression NVARCHAR(1000)   NULL,
    ConcurrencyPolicy     NVARCHAR(32)     NOT NULL DEFAULT N'Queue',
    FilterExpression      NVARCHAR(2000)   NULL,
    CreatedAt             DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_TriggerRegistrations_WorkflowDefinitions
        FOREIGN KEY (WorkflowDefinitionId) REFERENCES dbo.WorkflowDefinitions (Id)
);
GO

CREATE INDEX IX_TriggerRegistrations_TriggerKey
    ON dbo.TriggerRegistrations (TriggerKey)
    INCLUDE (WorkflowDefinitionId, TriggerNodeId, ConcurrencyPolicy);
GO

CREATE INDEX IX_TriggerRegistrations_WorkflowDefinitionId
    ON dbo.TriggerRegistrations (WorkflowDefinitionId);
GO
