-- The runs the stats count: started (CreatedAt) in [@FromUtc, @ToUtc), test runs left out, for one
-- workspace (@WorkspaceId) or one workflow (@WorkflowRefId); pass the other NULL. Windowed on CreatedAt,
-- so "runs started in this period", not "runs that finished in it": a long run started before the
-- window would otherwise appear or vanish depending on when it happened to complete.
--   WorkflowRefId  the workflow
--   Day            the UTC day the run started
--   IsWaiting      still Running, with every live branch waiting on a delay, signal or call
--                  (dbo.RunParked); probed only by a caller that reads it
--   DurationMs     NULL until the run completes, so an average or percentile counts finished runs only
-- Inline, with one branch per scope behind a startup filter, so each caller's plan seeks the index it
-- had. The five stats procedures select from it, so their window cannot drift apart.
CREATE FUNCTION dbo.RunsInWindow
(
    @WorkspaceId   INT,
    @WorkflowRefId UNIQUEIDENTIFIER,
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3)
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        r.WorkflowRefId,
        CAST(r.CreatedAt AS DATE) AS Day,
        r.Status,
        CASE WHEN r.Status = N'Running' AND EXISTS (SELECT 1 FROM dbo.RunParked(r.Id)) THEN 1 ELSE 0 END AS IsWaiting,
        CASE WHEN r.CompletedAt IS NULL THEN NULL ELSE DATEDIFF_BIG(MILLISECOND, r.StartedAt, r.CompletedAt) END AS DurationMs
    FROM dbo.Runs r
    WHERE @WorkspaceId IS NOT NULL
      AND r.WorkspaceId = @WorkspaceId
      AND r.CreatedAt >= @FromUtc
      AND r.CreatedAt < @ToUtc
      AND r.IsTest = 0

    UNION ALL

    SELECT
        r.WorkflowRefId,
        CAST(r.CreatedAt AS DATE),
        r.Status,
        CASE WHEN r.Status = N'Running' AND EXISTS (SELECT 1 FROM dbo.RunParked(r.Id)) THEN 1 ELSE 0 END,
        CASE WHEN r.CompletedAt IS NULL THEN NULL ELSE DATEDIFF_BIG(MILLISECOND, r.StartedAt, r.CompletedAt) END
    FROM dbo.Runs r
    WHERE @WorkspaceId IS NULL
      AND r.WorkflowRefId = @WorkflowRefId
      AND r.CreatedAt >= @FromUtc
      AND r.CreatedAt < @ToUtc
      AND r.IsTest = 0
);
GO
