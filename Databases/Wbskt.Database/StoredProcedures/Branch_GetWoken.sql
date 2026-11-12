CREATE PROCEDURE dbo.Branch_GetWoken
    @AfterId   BIGINT,
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Waiting branches of a live run whose bookmark a claim deleted to wake them (WokenAt is written in
    -- the claim's own transaction, and cleared by the next write that moves the branch). Each one's
    -- wake is stored on it, so resuming it does what the lost dispatch would have done. A branch left
    -- Waiting with no bookmark and no WokenAt was stopped mid-park instead; it is not listed here, and
    -- the orphan GC deals with it. Paged by Id, after @AfterId.
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
        br.CreatedAt,
        br.UpdatedAt,
        br.RowVersion
    FROM dbo.Branches br
    INNER JOIN dbo.Runs r ON r.Id = br.RunId
    WHERE br.WokenAt IS NOT NULL
      AND br.Id > @AfterId
      AND br.Status = N'Waiting'
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
    ORDER BY br.Id;
END;
GO
