CREATE PROCEDURE dbo.Run_GetStatsBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    -- Same shape as Run_GetStatsBy_WorkflowRefId, over the same window (dbo.RunsInWindow).
    WITH Windowed AS
    (
        SELECT w.Status, w.IsWaiting, w.DurationMs
        FROM dbo.RunsInWindow(@WorkspaceId, NULL, @FromUtc, @ToUtc) w
    ),
    Durations AS
    (
        -- Completed runs only; counting in-flight ones as zero would drag every percentile down.
        SELECT DISTINCT
            PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DurationMs) OVER () AS P50DurationMs,
            PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DurationMs) OVER () AS P95DurationMs
        FROM Windowed
        WHERE DurationMs IS NOT NULL
    )
    SELECT
        COUNT(*)                                                                      AS TotalRuns,
        SUM(CASE WHEN w.Status = N'Succeeded'       THEN 1 ELSE 0 END)                AS SucceededCount,
        SUM(CASE WHEN w.Status = N'Failed'          THEN 1 ELSE 0 END)                AS FailedCount,
        SUM(CASE WHEN w.Status = N'PartiallyFailed' THEN 1 ELSE 0 END)                AS PartiallyFailedCount,
        SUM(CASE WHEN w.Status = N'Cancelled'       THEN 1 ELSE 0 END)                AS CancelledCount,
        SUM(CASE WHEN w.Status = N'OutOfCredits'    THEN 1 ELSE 0 END)                AS OutOfCreditsCount,
        SUM(CASE WHEN w.Status IN (N'Running', N'Failing', N'Cancelling') THEN 1 ELSE 0 END) AS ActiveCount,
        SUM(w.IsWaiting)                                                              AS WaitingCount,
        ISNULL(MAX(w.DurationMs), 0)                                                  AS MaxDurationMs,
        ISNULL(AVG(w.DurationMs), 0)                                                  AS AvgDurationMs,
        ISNULL((SELECT TOP 1 P50DurationMs FROM Durations), 0)                        AS P50DurationMs,
        ISNULL((SELECT TOP 1 P95DurationMs FROM Durations), 0)                        AS P95DurationMs
    FROM Windowed w;
END;
GO
