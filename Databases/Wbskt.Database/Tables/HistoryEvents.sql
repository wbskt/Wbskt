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

-- Every read is by RunId (the history of one run, a page of runs' last steps), which the clustered key
-- serves, and retention deletes by run. An index on HistoryEventId alone or on Timestamp had no reader
-- and cost a write on every step of every run.
