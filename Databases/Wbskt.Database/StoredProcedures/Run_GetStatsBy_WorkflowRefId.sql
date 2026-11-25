CREATE PROCEDURE dbo.Run_GetStatsBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    -- Percentiles are over COMPLETED runs only: counting in-flight ones as zero would drag every
    -- percentile down and make a busy workflow look fast. They are PERCENTILE_CONT's answer (linear
    -- interpolation between the two closest ranks) worked out from one sort: the count first, then
    -- the values at the two ranks each percentile falls between. PERCENTILE_CONT ... OVER () spooled
    -- the whole window several times and was the bulk of this procedure's cost (#1116).
    DECLARE @Completed BIGINT =
    (
        SELECT COUNT_BIG(*)
        FROM dbo.RunsInWindow(NULL, @WorkflowRefId, @FromUtc, @ToUtc)
        WHERE DurationMs IS NOT NULL
    );
    DECLARE @P50Rank FLOAT = (@Completed - 1) * 0.50;
    DECLARE @P95Rank FLOAT = (@Completed - 1) * 0.95;
    DECLARE @P50DurationMs FLOAT;
    DECLARE @P95DurationMs FLOAT;

    WITH Ranked AS
    (
        SELECT
            DurationMs,
            ROW_NUMBER() OVER (ORDER BY DurationMs) - 1 AS Position
        FROM dbo.RunsInWindow(NULL, @WorkflowRefId, @FromUtc, @ToUtc)
        WHERE DurationMs IS NOT NULL
    ),
    Bounds AS
    (
        SELECT
            MAX(CASE WHEN Position = FLOOR(@P50Rank)   THEN DurationMs END) AS P50Low,
            MAX(CASE WHEN Position = CEILING(@P50Rank) THEN DurationMs END) AS P50High,
            MAX(CASE WHEN Position = FLOOR(@P95Rank)   THEN DurationMs END) AS P95Low,
            MAX(CASE WHEN Position = CEILING(@P95Rank) THEN DurationMs END) AS P95High
        FROM Ranked
        WHERE Position IN (FLOOR(@P50Rank), CEILING(@P50Rank), FLOOR(@P95Rank), CEILING(@P95Rank))
    )
    SELECT
        @P50DurationMs = P50Low + (P50High - P50Low) * (@P50Rank - FLOOR(@P50Rank)),
        @P95DurationMs = P95Low + (P95High - P95Low) * (@P95Rank - FLOOR(@P95Rank))
    FROM Bounds;

    -- The runs started in the window (dbo.RunsInWindow says why on CreatedAt).
    WITH Windowed AS
    (
        SELECT w.Status, w.IsWaiting, w.DurationMs
        FROM dbo.RunsInWindow(NULL, @WorkflowRefId, @FromUtc, @ToUtc) w
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
        ISNULL(@P50DurationMs, 0)                                                     AS P50DurationMs,
        ISNULL(@P95DurationMs, 0)                                                     AS P95DurationMs
    FROM Windowed w;
END;
GO
