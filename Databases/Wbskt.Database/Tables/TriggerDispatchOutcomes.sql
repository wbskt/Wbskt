-- What each workflow trigger did with the latest messages that reached it: started a run, filtered
-- it out, queued it, dropped it, held it. Answers "I sent the message and nothing happened" (#416).
-- A ring buffer, not a log: TriggerDispatchOutcome_Record keeps only the newest @Keep rows for each
-- trigger node, so the table stays at (triggers x @Keep) rows however busy a device is.
CREATE TABLE dbo.TriggerDispatchOutcomes
(
    Id             BIGINT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
    WorkflowRefId  UNIQUEIDENTIFIER NOT NULL,
    TriggerNodeId  UNIQUEIDENTIFIER NOT NULL,
    OccurredAt     DATETIME2(3)     NOT NULL,
    ChannelKind    NVARCHAR(64)     NOT NULL,
    MessageType    NVARCHAR(200)    NULL,
    Outcome        NVARCHAR(32)     NOT NULL,
    CorrelationKey NVARCHAR(400)    NULL,
    RunRefId       UNIQUEIDENTIFIER NULL
);
GO

CREATE INDEX IX_TriggerDispatchOutcomes_Trigger
    ON dbo.TriggerDispatchOutcomes (WorkflowRefId, TriggerNodeId, Id DESC);
GO
