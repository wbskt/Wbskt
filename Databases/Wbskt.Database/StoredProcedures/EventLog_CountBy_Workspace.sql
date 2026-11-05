-- How many entries a workspace logged in [@FromUtc, @ToUtc), for the audit log's view counts, by
-- event, by who caused it and by where it came from. The caller folds these into its groups, people
-- and sources, so a new event or group needs no change here.
-- @IncludeClientTraffic = 0 skips the traffic table, for a summary that leaves traffic out.
CREATE PROCEDURE dbo.EventLog_CountBy_Workspace
    @WorkspaceId INT,
    @FromUtc DATETIME2(3),
    @ToUtc DATETIME2(3),
    @IncludeClientTraffic BIT = 1,
    -- Old name of @IncludeClientTraffic, accepted for one release while a deploy rolls (wbskt/Wbskt#583).
    @IncludeDeviceTraffic BIT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SET @IncludeClientTraffic = COALESCE(@IncludeDeviceTraffic, @IncludeClientTraffic);

    SELECT e.EventName, el.UserRefId, el.Source, COUNT_BIG(*) AS EntryCount
    FROM dbo.EventLogEntries el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@IncludeClientTraffic = 1 OR el.IsClientTraffic = 0)
      AND el.CreatedAt >= @FromUtc
      AND el.CreatedAt < @ToUtc
    GROUP BY e.EventName, el.UserRefId, el.Source
    OPTION (RECOMPILE);
END
GO
