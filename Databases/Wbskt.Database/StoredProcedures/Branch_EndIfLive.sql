CREATE PROCEDURE dbo.Branch_EndIfLive
    @Id             INT,
    @ToStatus       NVARCHAR(32),
    @LastOutputJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    -- A branch ends once. Two loops can reach the end of the same branch: a cancel that marked a
    -- waiting branch Cancelled while a bookmark wake for it was in flight, or a recovery requeue of
    -- an Active branch a slow node on the old leader was still finishing. An unconditional write let
    -- the second end overwrite the first (Completed over Cancelled) and release the branch twice, and
    -- the extra decrement could finalise the run while another branch was still running. The status
    -- guard makes the end a compare-and-set, like Run_TransitionStatus: the caller releases the
    -- branch only when this returns 1.
    UPDATE dbo.Branches WITH (ROWLOCK)
    SET
        Status = @ToStatus,
        LastOutputJson = @LastOutputJson,
        PendingTakePort = NULL,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND Status IN ('Active', 'Waiting', 'Compensating');

    SELECT @@ROWCOUNT;
END;
GO
