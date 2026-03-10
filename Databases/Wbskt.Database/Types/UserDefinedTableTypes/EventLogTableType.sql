CREATE TYPE [dbo].[EventLogTableType] AS TABLE
(
    [EventId] INT,
    [EventData] NVARCHAR(MAX),
    [CreatedAtUtc] DATETIME2(0),
    [WorkspaceId] INT
)
