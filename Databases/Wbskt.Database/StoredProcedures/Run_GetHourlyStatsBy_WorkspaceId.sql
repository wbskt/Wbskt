-- Run outcomes per UTC hour across a workspace, for a chart of the last day. The same rules and window
-- as Run_GetDailyStatsBy_WorkspaceId (dbo.RunsInWindow); hours without runs are left out.
CREATE PROCEDURE dbo.Run_GetHourlyStatsBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        w.[Hour],
        COUNT(*)                                                                                AS TotalRuns,
        SUM(CASE WHEN w.Status = N'Succeeded' THEN 1 ELSE 0 END)                                AS SucceededCount,
        SUM(CASE WHEN w.Status IN (N'Failed', N'PartiallyFailed', N'OutOfCredits') THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN w.Status = N'Cancelled' THEN 1 ELSE 0 END)                                AS CancelledCount
    FROM dbo.RunsInWindow(@WorkspaceId, NULL, @FromUtc, @ToUtc) w
    GROUP BY w.[Hour];
END;
GO
