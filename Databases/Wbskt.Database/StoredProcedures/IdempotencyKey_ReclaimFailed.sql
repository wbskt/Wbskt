CREATE PROCEDURE dbo.IdempotencyKey_ReclaimFailed
    @KeyValue       NVARCHAR(200),
    @NewBranchRefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- A 'Failed' claim row means a prior caller crashed mid-resume (e.g. a bookmark match threw)
    -- and never got to MarkSucceeded/MarkFailed cleanly, or explicitly recorded the failure via
    -- MarkFailedAsync. Either way the event was never actually delivered, so the row must be
    -- reclaimable rather than wedged forever as a false "already handled" claim.
    -- Only one caller can win: the UPDATE only matches while Status is still 'Failed', so a
    -- concurrent reclaimer's UPDATE affects 0 rows and its SELECT reveals the winner's BranchRefId.
    UPDATE dbo.IdempotencyKeys WITH (ROWLOCK)
    SET BranchRefId = @NewBranchRefId,
        Status = N'Pending',
        ErrorJson = NULL,
        CompletedAt = NULL
    WHERE KeyValue = @KeyValue
      AND Status = N'Failed';

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
