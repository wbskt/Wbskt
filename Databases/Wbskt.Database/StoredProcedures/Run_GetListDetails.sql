-- What a run list shows beyond the run row, for one page of runs: credits used, where a failed run
-- first failed, the last step a succeeded run completed, and the step a live run is on with its
-- retry attempt. One call per page instead of a history read per run.
CREATE PROCEDURE dbo.Run_GetListDetails
    @RunIds dbo.IdTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        r.Id AS RunId,
        rc.CreditsConsumed,
        failed.NodeId AS FailedNodeId,
        failed.ErrorCode AS FailedErrorCode,
        ended.NodeId AS EndedNodeId,
        live.NodeId AS CurrentNodeId,
        -- The journal records the attempt that failed; the one under way is the next.
        CASE WHEN lastStep.EventKind = N'NodeRetrying' THEN lastStep.Attempt + 1 END AS CurrentAttempt,
        CASE WHEN lastStep.EventKind = N'NodeRetrying' THEN lastStep.MaxAttempts END AS CurrentMaxAttempts
    FROM @RunIds ids
    INNER JOIN dbo.Runs r ON r.Id = ids.Id
    LEFT JOIN dbo.RunCounters rc ON rc.RunId = r.Id
    OUTER APPLY (
        SELECT TOP (1) h.NodeId, JSON_VALUE(h.PayloadJson, '$.errorCode') AS ErrorCode
        FROM dbo.HistoryEvents h
        WHERE h.RunId = r.Id AND h.EventKind = N'NodeFailed'
          AND r.Status IN (N'Failed', N'PartiallyFailed', N'Failing')
        ORDER BY h.HistoryEventId
    ) failed
    OUTER APPLY (
        SELECT TOP (1) h.NodeId
        FROM dbo.HistoryEvents h
        WHERE h.RunId = r.Id AND h.EventKind = N'NodeCompleted' AND r.Status = N'Succeeded'
        ORDER BY h.HistoryEventId DESC
    ) ended
    OUTER APPLY (
        SELECT TOP (1) b.RefId, b.NodeId
        FROM dbo.Branches b
        WHERE b.RunId = r.Id AND b.Status IN (N'Active', N'Waiting')
          AND r.Status IN (N'Running', N'Failing', N'Cancelling')
        ORDER BY b.UpdatedAt DESC
    ) live
    OUTER APPLY (
        SELECT TOP (1)
            h.EventKind,
            TRY_CAST(JSON_VALUE(h.PayloadJson, '$.attempt') AS INT) AS Attempt,
            TRY_CAST(JSON_VALUE(h.PayloadJson, '$.maxAttempts') AS INT) AS MaxAttempts
        FROM dbo.HistoryEvents h
        WHERE live.RefId IS NOT NULL AND h.RunId = r.Id AND h.BranchRefId = live.RefId AND h.NodeId = live.NodeId
          AND h.EventKind IN (N'NodeRetrying', N'NodeCompleted', N'NodeFailed')
        ORDER BY h.HistoryEventId DESC
    ) lastStep;
END;
GO
