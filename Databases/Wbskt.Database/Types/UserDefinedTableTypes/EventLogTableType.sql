CREATE TYPE dbo.EventLogTableType AS TABLE
(
    EventId       INT,
    EventData     NVARCHAR(MAX),
    CreatedAtUtc  DATETIME2(3),
    WorkspaceId   INT
)
GO
