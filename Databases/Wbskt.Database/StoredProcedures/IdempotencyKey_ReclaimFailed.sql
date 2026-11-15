CREATE PROCEDURE dbo.IdempotencyKey_ReclaimFailed
    @KeyValue                 NVARCHAR(200),
    @NewBranchRefId           UNIQUEIDENTIFIER,
    @PendingStaleAfterSeconds INT = 120
AS
BEGIN
    SET NOCOUNT ON;

    -- A 'Failed' claim row means a prior caller crashed mid-resume (e.g. a bookmark match threw)
    -- and never got to MarkSucceeded/MarkFailed cleanly, or explicitly recorded the failure via
    -- MarkFailedAsync. Either way the event was never actually delivered, so the row must be
    -- reclaimable rather than wedged forever as a false "already handled" claim.
    -- An inbound-event or trigger-start claim still 'Pending' long after it was taken is reclaimed too.
    -- Its holder failed and could not even record that (the write that marks it Failed failed as well),
    -- so every redelivery was dropped as a duplicate while the leader kept running; only a new leader's
    -- IdempotencyKey_ReleaseAbandonedClaims freed it. No dispatch holds its claim for minutes, and one
    -- that did would find its settle missing (the token is new) and leave the work to this claimant, as
    -- after that release. Action keys are left alone, as there.
    -- Only one caller can win: the UPDATE only matches while the row is still reclaimable, so a
    -- concurrent reclaimer's UPDATE affects 0 rows and its SELECT reveals the winner's BranchRefId.
    -- CreatedAt is the time the claim was taken, so a reclaim takes it again; the retention sweep reads
    -- it too and only keeps the row a little longer.
    DECLARE @PendingStaleBefore DATETIME2(3) = DATEADD(SECOND, -@PendingStaleAfterSeconds, SYSUTCDATETIME());

    UPDATE dbo.IdempotencyKeys WITH (ROWLOCK)
    SET BranchRefId = @NewBranchRefId,
        Status = N'Pending',
        ErrorJson = NULL,
        CompletedAt = NULL,
        CreatedAt = SYSUTCDATETIME()
    WHERE KeyValue = @KeyValue
      AND (Status = N'Failed'
           OR (Status = N'Pending'
               AND CreatedAt < @PendingStaleBefore
               AND (KeyValue LIKE N'inbound-event:%' OR KeyValue LIKE N'trigger-start:%')));

    SELECT
        Id,
        KeyValue,
        RunId,
        BranchRefId,
        NodeId,
        Attempt,
        Status,
        ResultJson,
        ErrorJson,
        CreatedAt,
        CompletedAt
    FROM dbo.IdempotencyKeys
    WHERE KeyValue = @KeyValue;
END;
GO
