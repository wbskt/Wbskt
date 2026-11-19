CREATE PROCEDURE dbo.Branch_GetRunning
    @AfterId   BIGINT,
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Branches are persisted as 'Active' while executing (never 'Running' - see BranchLoop.ActiveStatus).
    -- Recovery and the overlap re-check only dispatch these, so they get the ids and their run's status
    -- (one join, rather than a run read per group), not the branch's local state. Paged by Id, after
    -- @AfterId.
    SELECT TOP (@BatchSize)
        br.Id,
        br.RunId,
        br.UpdatedAt,
        r.Status AS RunStatus,
        r.CancellationReason AS RunCancellationReason
    FROM dbo.Branches br
    INNER JOIN dbo.Runs r ON r.Id = br.RunId
    WHERE br.Status = N'Active'
      AND br.Id > @AfterId
    ORDER BY br.Id;
END;
GO
