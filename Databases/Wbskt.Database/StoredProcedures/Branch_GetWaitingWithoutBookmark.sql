CREATE PROCEDURE dbo.Branch_GetWaitingWithoutBookmark
    @CutoffUtc DATETIME2(3),
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- A branch parks in two writes: Branch_Update to 'Waiting', then the bookmark insert. A crash or a
    -- throw between them leaves a Waiting branch that nothing will ever wake, and it keeps its run's
    -- ActiveBranchCount above zero, so the run never finalizes. Every wait parks this way - a Delay
    -- parks on a timer bookmark like any other - so "Waiting with no bookmark" is the stranded state
    -- for all of them, and the same state follows a wake whose claim deleted the bookmark but whose
    -- dispatch was lost. The cutoff is the grace period that keeps a branch mid-park out of this list;
    -- the caller still re-checks each row before acting on it.
    SELECT TOP (@BatchSize)
        br.Id,
        br.RefId,
        br.RunId,
        br.ParentBranchId,
        br.ForkCohortId,
        br.NodeId,
        br.Status,
        br.PendingTakePort,
        br.LocalJson,
        br.LastOutputJson,
        br.CompensationStackJson,
        br.CreatedAt,
        br.UpdatedAt,
        br.RowVersion
    FROM dbo.Branches br
    INNER JOIN dbo.Runs r ON r.Id = br.RunId
    WHERE br.Status = N'Waiting'
      AND br.UpdatedAt < @CutoffUtc
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
      AND NOT EXISTS (SELECT 1 FROM dbo.Bookmarks b
                      WHERE b.RunId = br.RunId
                        AND b.BranchRefId = br.RefId)
    ORDER BY br.UpdatedAt;
END;
GO
