-- Run outcomes per workflow per UTC day across a workspace: every explorer card's 30-day strip in one
-- call, and summed by the caller for the workspace's own strip. The same rules as
-- Run_GetDailyStatsBy_WorkflowRefId. Grouped on the definition's RefId so the read stays on
-- IX_Runs_WorkflowDefinitionId_CreatedAt, which includes Status but not the run's WorkflowRefId.
CREATE PROCEDURE dbo.Run_GetDailyStatsBy_WorkspaceId
    @WorkspaceId INT,
    @FromUtc     DATETIME2(3),
    @ToUtc       DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        d.RefId                                                                                 AS WorkflowRefId,
        CAST(r.CreatedAt AS DATE)                                                               AS Day,
        COUNT(*)                                                                                AS TotalRuns,
        SUM(CASE WHEN r.Status = N'Succeeded' THEN 1 ELSE 0 END)                                AS SucceededCount,
        SUM(CASE WHEN r.Status IN (N'Failed', N'PartiallyFailed', N'OutOfCredits') THEN 1 ELSE 0 END) AS FailedCount,
        SUM(CASE WHEN r.Status = N'Cancelled' THEN 1 ELSE 0 END)                                AS CancelledCount
    FROM dbo.Runs r
    INNER JOIN dbo.WorkflowDefinitions d ON d.Id = r.WorkflowDefinitionId
    WHERE d.WorkspaceId = @WorkspaceId
      AND r.CreatedAt >= @FromUtc
      AND r.CreatedAt < @ToUtc
      AND r.IsTest = 0
    GROUP BY d.RefId, CAST(r.CreatedAt AS DATE);
END;
GO
