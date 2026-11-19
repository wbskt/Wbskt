-- Run outcomes per workflow per UTC day across a workspace: every explorer card's 30-day strip in one
-- call, and summed by the caller for the workspace's own strip. The same rules as
-- Run_GetDailyStatsBy_WorkflowRefId, over the same window (dbo.RunsInWindow).
CREATE PROCEDURE dbo.Run_GetDailyStatsBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        w.WorkflowRefId,
        w.Day,
        COUNT(*)                                                                                AS TotalRuns,
        SUM(CASE WHEN w.Status = N'Succeeded' THEN 1 ELSE 0 END)                                AS SucceededCount,
        SUM(CASE WHEN w.Status IN (N'Failed', N'PartiallyFailed', N'OutOfCredits') THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN w.Status = N'Cancelled' THEN 1 ELSE 0 END)                                AS CancelledCount
    FROM dbo.RunsInWindow(@WorkspaceId, NULL, @FromUtc, @ToUtc) w
    GROUP BY w.WorkflowRefId, w.Day;
END;
GO
