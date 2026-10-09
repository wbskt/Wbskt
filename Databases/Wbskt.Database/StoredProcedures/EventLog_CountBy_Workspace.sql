-- How many entries of each event a workspace logged in [@FromUtc, @ToUtc), for the audit log's view
-- counts. The caller folds the events into its groups, so a new event or group needs no change here.
CREATE PROCEDURE dbo.EventLog_CountBy_Workspace
    @WorkspaceId INT,
    @FromUtc DATETIME2(3),
    @ToUtc DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EventName, COUNT_BIG(*) AS EntryCount
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND el.CreatedAt >= @FromUtc
      AND el.CreatedAt < @ToUtc
    GROUP BY e.EventName;
END
GO
