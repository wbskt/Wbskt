CREATE PROCEDURE dbo.Branch_GetWoken
    @AfterId       BIGINT,
    @BatchSize     INT,
    @UpdatedBefore DATETIME2(3) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Waiting branches of a live run whose bookmark a claim deleted to wake them (WokenAt is written in
    -- the claim's own transaction, and cleared by the next write that moves the branch). Each one's
    -- wake is stored on it, so resuming it does what the lost dispatch would have done. A branch left
    -- Waiting with no bookmark and no WokenAt was stopped mid-park instead; it is not listed here, and
    -- the orphan GC deals with it. Paged by Id, after @AfterId. A new leader reads them all; the orphan
    -- GC passes @UpdatedBefore to read only those whose wake has waited past its grace period.
    -- Only what dispatching one takes, and the row version the orphan GC compares to tell a branch it
    -- already re-dispatched; the branch loop reads the rest itself.
    SELECT TOP (@BatchSize)
        br.Id,
        br.RunId,
        br.RowVersion
    FROM dbo.Branches br
    INNER JOIN dbo.Runs r ON r.Id = br.RunId
    WHERE br.WokenAt IS NOT NULL
      AND br.Id > @AfterId
      AND br.Status = N'Waiting'
      AND (@UpdatedBefore IS NULL OR br.UpdatedAt < @UpdatedBefore)
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
    ORDER BY br.Id;
END;
GO
