CREATE PROCEDURE dbo.Run_GetNodeTimingsBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @FromUtc       DATETIME2(3),
    @ToUtc         DATETIME2(3),
    @Top           INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Answers "which step in MY workflow is slow" - the question the Prometheus histogram cannot,
    -- because it is tagged by node KIND, not node id. Reads the durationMs the branch loop puts on
    -- every node outcome; slowest average first.
    SELECT TOP (@Top)
        h.NodeId                                                            AS NodeId,
        COUNT(*)                                                            AS Executions,
        SUM(CASE WHEN h.EventKind = N'NodeFailed' THEN 1 ELSE 0 END)        AS FailureCount,
        AVG(TRY_CAST(JSON_VALUE(h.PayloadJson, '$.durationMs') AS FLOAT))   AS AvgDurationMs,
        MAX(TRY_CAST(JSON_VALUE(h.PayloadJson, '$.durationMs') AS FLOAT))   AS MaxDurationMs
    FROM dbo.HistoryEvents h
    INNER JOIN dbo.Runs r ON r.Id = h.RunId
    WHERE r.WorkflowRefId = @WorkflowRefId
      AND r.CreatedAt >= @FromUtc
      AND r.CreatedAt < @ToUtc
      AND h.EventKind IN (N'NodeCompleted', N'NodeFailed')
      AND h.NodeId IS NOT NULL
      AND TRY_CAST(JSON_VALUE(h.PayloadJson, '$.durationMs') AS FLOAT) IS NOT NULL
    GROUP BY h.NodeId
    ORDER BY AVG(TRY_CAST(JSON_VALUE(h.PayloadJson, '$.durationMs') AS FLOAT)) DESC;
END;
GO
