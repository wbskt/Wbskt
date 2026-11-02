-- A batch of trigger outcomes, in the order they happened (Ordinal ascending).
CREATE TYPE dbo.TriggerDispatchOutcomeTableType AS TABLE
(
    Ordinal        INT              NOT NULL PRIMARY KEY,
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
