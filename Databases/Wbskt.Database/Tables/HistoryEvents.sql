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

-- A run's failures without its other steps: the workflow page's top failures and the runs list's
-- "failed at" read only NodeFailed rows, and through the clustered key they read every step of every
-- run in the window, payloads included, to find them (27k reads and 2.4 s a call on dev, #1113).
-- Filtered, so only a failure pays the write; the clustered key's HistoryEventId rides along after
-- RunId, so "the first failure of this run" is still a seek.
CREATE INDEX IX_HistoryEvents_RunId_NodeFailed
    ON dbo.HistoryEvents (RunId)
    INCLUDE (NodeId, Timestamp, PayloadJson)
    WHERE EventKind = N'NodeFailed';
GO
