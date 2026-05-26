CREATE TABLE dbo.HistoryEvents
(
    HistoryEventId BIGINT           NOT NULL IDENTITY(1,1),
    RunId          INT              NOT NULL,
    BranchRefId    UNIQUEIDENTIFIER NULL,
    NodeId         UNIQUEIDENTIFIER NULL,
    EventKind      NVARCHAR(64)     NOT NULL,
    Severity       NVARCHAR(16)     NOT NULL,
    PayloadJson    NVARCHAR(MAX)    NULL,
    Timestamp      DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_HistoryEvents PRIMARY KEY CLUSTERED (RunId, HistoryEventId)
);
GO

CREATE INDEX IX_HistoryEvents_HistoryEventId
    ON dbo.HistoryEvents (HistoryEventId);
GO

CREATE INDEX IX_HistoryEvents_Timestamp_Severity
    ON dbo.HistoryEvents (Timestamp, Severity);
GO
