CREATE PROCEDURE dbo.Run_GetEndedChildrenAwaited
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Child runs that ended before the cutoff while a parent's sub-workflow step still waits on them:
    -- the child-completed event that should have woken the parent was never raised (the finalizer
    -- stopped between the terminal status and the hook) or arrived before the parent parked. The
    -- cutoff keeps a child whose event is still on its way out of the list. The status list must name
    -- every terminal status in RunStatus.IsTerminal. The LIKE turns the read into a range seek on
    -- IX_Bookmarks_MatchKey rather than a scan of every bookmark.
    SELECT DISTINCT TOP (@BatchSize)
        c.Id,
        c.RefId,
        c.WorkflowDefinitionId,
        c.WorkflowRefId,
        c.WorkflowVersion,
        c.TriggerNodeId,
        c.CorrelationKey,
        c.Status,
        c.StartedAt,
        c.CompletedAt,
        c.CancellationRequestedAt,
        c.CancellationReason,
        c.CreditBudget,
        c.CreatedAt,
        c.ParentRunRefId,
        c.OutputJson
    FROM dbo.Bookmarks b
    INNER JOIN dbo.Runs c ON c.RefId = TRY_CONVERT(UNIQUEIDENTIFIER, SUBSTRING(b.MatchKey, LEN(N'child-completed:') + 1, 36))
    WHERE b.MatchKey LIKE N'child-completed:%'
      AND b.WakeConditionKind = N'childRunCompleted'
      AND c.Status IN (N'Succeeded', N'Failed', N'PartiallyFailed', N'Cancelled', N'OutOfCredits')
      AND c.CompletedAt < @CutoffUtc
    ORDER BY c.CompletedAt;
END;
GO
