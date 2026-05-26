CREATE TABLE dbo.PendingTriggerEvents
(
    Id               INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowRefId    UNIQUEIDENTIFIER NOT NULL,
    TriggerNodeId    UNIQUEIDENTIFIER NOT NULL,
    CorrelationKey   NVARCHAR(400)    NOT NULL,
    InboundEventJson NVARCHAR(MAX)    NOT NULL,
    EnqueuedAt       DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedAt        DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

CREATE INDEX IX_PendingTriggerEvents_RunKey
    ON dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, EnqueuedAt);
GO
