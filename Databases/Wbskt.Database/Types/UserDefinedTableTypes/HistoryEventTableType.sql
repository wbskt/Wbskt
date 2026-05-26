CREATE TYPE dbo.HistoryEventTableType AS TABLE
(
    RunId       INT              NOT NULL,
    BranchRefId UNIQUEIDENTIFIER NULL,
    NodeId      UNIQUEIDENTIFIER NULL,
    EventKind   NVARCHAR(64)     NOT NULL,
    Severity    NVARCHAR(16)     NOT NULL,
    PayloadJson NVARCHAR(MAX)    NULL,
    Timestamp   DATETIME2(3)     NOT NULL
);
GO
