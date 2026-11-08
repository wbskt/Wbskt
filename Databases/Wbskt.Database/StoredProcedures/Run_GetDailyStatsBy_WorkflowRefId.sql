-- Run outcomes and the typical run time per UTC day for one workflow: the 30-day strip and the Runs
-- tab's time-taken sparkline. Windowed on CreatedAt like Run_GetStatsBy_WorkflowRefId, so a run counts
-- on the day it started. Only days with runs are returned; the caller fills the rest with zeros.
-- IX_Runs_WorkflowRefId_CreatedAt covers it (Status, StartedAt and CompletedAt are included), and unlike
-- the outcome stats there is no per-run branch probe.
CREATE PROCEDURE dbo.Run_GetDailyStatsBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    WITH Windowed AS
    (
        SELECT
            CAST(CreatedAt AS DATE) AS Day,
            Status,
            CASE
                WHEN CompletedAt IS NULL THEN NULL
                ELSE DATEDIFF_BIG(MILLISECOND, StartedAt, CompletedAt)
            END AS DurationMs
        FROM dbo.Runs
        WHERE WorkflowRefId = @WorkflowRefId
          AND CreatedAt >= @FromUtc
          AND CreatedAt < @ToUtc
    ),
    Medians AS
    (
        -- Completed runs only, as in the window's own percentiles; a day with none has no median.
        SELECT DISTINCT
            Day,
            PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DurationMs) OVER (PARTITION BY Day) AS P50DurationMs
        FROM Windowed
        WHERE DurationMs IS NOT NULL
    )
    SELECT
        @WorkflowRefId                                                                             AS WorkflowRefId,
        w.Day,
        COUNT(*)                                                                                   AS TotalRuns,
        SUM(CASE WHEN w.Status = N'Succeeded' THEN 1 ELSE 0 END)                                   AS SucceededCount,
        SUM(CASE WHEN w.Status IN (N'Failed', N'PartiallyFailed', N'OutOfCredits') THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN w.Status = N'Cancelled' THEN 1 ELSE 0 END)                                   AS CancelledCount,
        MAX(m.P50DurationMs)                                                                       AS P50DurationMs
    FROM Windowed w
    LEFT JOIN Medians m ON m.Day = w.Day
    GROUP BY w.Day;
END;
GO
