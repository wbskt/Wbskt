CREATE PROCEDURE dbo.Run_GetStatsPerWorkflowBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3),
    @Top         INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    -- The per-workflow breakdown that makes the workspace rollup honest. A workspace-wide success rate
    -- is run-weighted, so one high-volume workflow at 99% hides a low-volume one at 0% - the rollup is
    -- only safe to read alongside this.
    --
    -- Ordered by volume rather than by failure rate: this is the denominator view. A workflow with two
    -- runs, both failed, sorts last here by design; its 0% shows up in the row, not in the ordering.
    WITH Windowed AS
    (
        SELECT
            r.WorkflowRefId,
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
    )
    SELECT TOP (@Top)
        w.WorkflowRefId,
        COUNT(*)                                                                      AS TotalRuns,
        SUM(CASE WHEN w.Status = N'Succeeded' THEN 1 ELSE 0 END)                      AS SucceededCount,
        SUM(CASE WHEN w.Status IN (N'Failed', N'PartiallyFailed', N'Faulted', N'OutOfCredits') THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN w.Status IN (N'Running', N'Failing', N'Cancelling') THEN 1 ELSE 0 END) AS ActiveCount,
        ISNULL(AVG(w.DurationMs), 0)                                                  AS AvgDurationMs
    FROM Windowed w
    GROUP BY w.WorkflowRefId
    ORDER BY COUNT(*) DESC;
END;
GO
