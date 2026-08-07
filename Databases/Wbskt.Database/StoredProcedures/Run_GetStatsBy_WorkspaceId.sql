CREATE PROCEDURE dbo.Run_GetStatsBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    -- Same shape and the same windowing rule as Run_GetStatsBy_WorkflowRefId: on CreatedAt, so this is
    -- "runs started in this period". Runs carry no workspace of their own - it lives on the definition -
    -- so this joins through WorkflowDefinitionId, which IX_Runs_WorkflowDefinitionId_Id covers. The join
    -- is 1:1 (a run points at exactly one definition row), so nothing fans out.
    WITH Windowed AS
    (
        SELECT
            r.Status,
            CASE
                WHEN r.CompletedAt IS NULL THEN NULL
                ELSE DATEDIFF_BIG(MILLISECOND, r.StartedAt, r.CompletedAt)
            END AS DurationMs
        FROM dbo.Runs r
        INNER JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
        WHERE d.WorkspaceId = @WorkspaceId
          AND r.CreatedAt >= @FromUtc
          AND r.CreatedAt < @ToUtc
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
        SUM(CASE WHEN w.Status = N'Faulted'         THEN 1 ELSE 0 END)                AS FaultedCount,
        SUM(CASE WHEN w.Status = N'OutOfCredits'    THEN 1 ELSE 0 END)                AS OutOfCreditsCount,
        SUM(CASE WHEN w.Status IN (N'Running', N'Failing', N'Cancelling') THEN 1 ELSE 0 END) AS ActiveCount,
        ISNULL(MAX(w.DurationMs), 0)                                                  AS MaxDurationMs,
        ISNULL(AVG(w.DurationMs), 0)                                                  AS AvgDurationMs,
        ISNULL((SELECT TOP 1 P50DurationMs FROM Durations), 0)                        AS P50DurationMs,
        ISNULL((SELECT TOP 1 P95DurationMs FROM Durations), 0)                        AS P95DurationMs
    FROM Windowed w;
END;
GO
