CREATE PROCEDURE dbo.IdempotencyKey_FailAbandonedClaims
AS
BEGIN
    SET NOCOUNT ON;

    -- Called by a new engine leader before its bus starts, so no claim of its own exists yet: every
    -- Pending inbound-event or trigger-start claim was left by an engine that stopped mid-dispatch.
    -- Marking it Failed lets the broker's redelivery reclaim it (IdempotencyKey_ReclaimFailed) instead of
    -- dropping the event as a duplicate. Action keys are left alone: a Pending one already means "run
    -- the node again", and Failed would replay a failure that never happened.
    UPDATE dbo.IdempotencyKeys
    SET Status = N'Failed',
        ErrorJson = N'{"error":"abandoned by a stopped engine"}',
        CompletedAt = SYSUTCDATETIME()
    WHERE Status = N'Pending'
      AND (KeyValue LIKE N'inbound-event:%' OR KeyValue LIKE N'trigger-start:%');

    SELECT @@ROWCOUNT;
END;
GO
