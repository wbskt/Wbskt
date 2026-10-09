-- How many entries a workspace logged in [@FromUtc, @ToUtc), for the audit log's view counts, by
-- event, by who caused it and by where it came from. The caller folds these into its groups, people
-- and sources, so a new event or group needs no change here.
CREATE PROCEDURE dbo.EventLog_CountBy_Workspace
    @WorkspaceId INT,
    @FromUtc DATETIME2(3),
    @ToUtc DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT e.EventName, el.UserRefId, el.Source, COUNT_BIG(*) AS EntryCount
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND el.CreatedAt >= @FromUtc
      AND el.CreatedAt < @ToUtc
    GROUP BY e.EventName, el.UserRefId, el.Source;
END
GO
