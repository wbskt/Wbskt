-- A workspace's event log, newest first, one page at a time. @CursorId is the Id of the last row of
-- the previous page (NULL for the first); the caller asks for one row more than it shows to learn
-- whether there is a next page. No total count: counting every matching row on every page was the
-- expensive part, and nothing needs it. The CSV export reads through here too, with @Take as its cap.
--
-- @EventNames and @ExcludeEventNames are comma-separated exact names (event names have no commas):
-- the first keeps only those events (a view such as "policies"), the second drops them (device
-- traffic). @EventName is the older single filter, matched as a substring. @Sources is a
-- comma-separated list of EventSource values. @SinceId keeps only entries newer than the newest one
-- the caller already has, for a view that stays live by asking again.
CREATE PROCEDURE dbo.EventLog_GetBy_Workspace
    @WorkspaceId INT,
    @EventName NVARCHAR(100) = NULL,
    @EventNames NVARCHAR(MAX) = NULL,
    @ExcludeEventNames NVARCHAR(MAX) = NULL,
    @Criticality TINYINT = NULL,
    @MinCriticality TINYINT = NULL,
    @Sources NVARCHAR(100) = NULL,
    @SinceId BIGINT = NULL,
    @PolicyId INT = NULL,
    @ClientId INT = NULL,
    @WorkflowRefId UNIQUEIDENTIFIER = NULL,
    @UserRefId UNIQUEIDENTIFIER = NULL,
    @FromUtc DATETIME2(3) = NULL,
    @ToUtc DATETIME2(3) = NULL,
    -- Already escaped for LIKE by the caller, with '\' as the escape character.
    @Search NVARCHAR(200) = NULL,
    @CursorId BIGINT = NULL,
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Include TABLE (EventName NVARCHAR(100) NOT NULL PRIMARY KEY);
    DECLARE @Exclude TABLE (EventName NVARCHAR(100) NOT NULL PRIMARY KEY);
    DECLARE @Source TABLE (Source TINYINT NOT NULL PRIMARY KEY);

    IF @EventNames IS NOT NULL
        INSERT INTO @Include (EventName)
        SELECT DISTINCT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@EventNames, N',') WHERE LTRIM(RTRIM(value)) <> N'';

    IF @ExcludeEventNames IS NOT NULL
        INSERT INTO @Exclude (EventName)
        SELECT DISTINCT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@ExcludeEventNames, N',') WHERE LTRIM(RTRIM(value)) <> N'';

    IF @Sources IS NOT NULL
        INSERT INTO @Source (Source)
        SELECT DISTINCT CAST(value AS TINYINT) FROM STRING_SPLIT(@Sources, N',') WHERE LTRIM(RTRIM(value)) <> N'';

    SELECT TOP (@Take)
        el.Id,
        e.EventName,
        el.EventData,
        e.EventCriticality,
        el.PolicyRefId,
        el.ClientRefId,
        el.WorkflowRefId,
        el.UserRefId,
        el.CreatedAt,
        el.Source,
        el.ClientAddress,
        el.UserAgent
    FROM dbo.EventLogs el
    JOIN dbo.Events e ON el.EventId = e.Id
    WHERE el.WorkspaceId = @WorkspaceId
      AND (@CursorId IS NULL OR el.Id < @CursorId)
      AND (@SinceId IS NULL OR el.Id > @SinceId)
      AND (@PolicyId IS NULL OR el.PolicyId = @PolicyId)
      AND (@ClientId IS NULL OR el.ClientId = @ClientId)
      AND (@WorkflowRefId IS NULL OR el.WorkflowRefId = @WorkflowRefId)
      AND (@UserRefId IS NULL OR el.UserRefId = @UserRefId)
      AND (@FromUtc IS NULL OR el.CreatedAt >= @FromUtc)
      AND (@ToUtc IS NULL OR el.CreatedAt < @ToUtc)
      AND (@EventName IS NULL OR e.EventName LIKE '%' + @EventName + '%')
      AND (@EventNames IS NULL OR e.EventName IN (SELECT EventName FROM @Include))
      AND (@ExcludeEventNames IS NULL OR e.EventName NOT IN (SELECT EventName FROM @Exclude))
      AND (@Criticality IS NULL OR e.EventCriticality = @Criticality)
      AND (@MinCriticality IS NULL OR e.EventCriticality >= @MinCriticality)
      AND (@Sources IS NULL OR el.Source IN (SELECT Source FROM @Source))
      AND (@Search IS NULL
           OR el.EventData LIKE N'%' + @Search + N'%' ESCAPE N'\'
           OR e.EventName LIKE N'%' + @Search + N'%' ESCAPE N'\')
    ORDER BY el.Id DESC
    OPTION (RECOMPILE);
END
GO
