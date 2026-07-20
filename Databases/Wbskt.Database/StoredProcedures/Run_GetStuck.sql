CREATE PROCEDURE dbo.Run_GetStuck
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- A run is stuck when it is non-terminal, old enough, AND has no live work:
    -- no bookmark rows and no branches that could still make progress.
    -- 'Active' branches are excluded because an Active branch mid-execution is legitimate;
    -- a *crashed* Active branch is handled by crash recovery (Branch_GetRunning), not the reaper.
    SELECT TOP (@BatchSize)
        r.Id,
        r.RefId,
        r.WorkflowDefinitionId,
        r.WorkflowRefId,
        r.WorkflowVersion,
        r.TriggerNodeId,
        r.CorrelationKey,
        r.Status,
        r.StartedAt,
        r.CompletedAt,
        r.CancellationRequestedAt,
        r.CancellationReason,
        r.CreditBudget,
        r.CreatedAt
    FROM dbo.Runs r
    WHERE r.Status IN (N'Running', N'Cancelling', N'Failing')
      AND r.CreatedAt < @CutoffUtc
      AND NOT EXISTS (SELECT 1 FROM dbo.Bookmarks b WHERE b.RunId = r.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.Branches br
                      WHERE br.RunId = r.Id
                        AND br.Status IN (N'Active', N'Waiting', N'WaitingAtJoin', N'Compensating'))
    ORDER BY r.CreatedAt;
END;
GO
