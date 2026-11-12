CREATE PROCEDURE dbo.IdempotencyKey_ReleaseAbandonedClaims
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Called by a new engine leader before its bus starts, so no claim of its own exists yet: a Pending
    -- inbound-event or trigger-start claim was left by an engine that stopped mid-dispatch, or, while an
    -- old leader overlaps the new one, is still being finished by it (the claim token on MarkSucceeded and
    -- MarkFailed keeps that one's late write off a claim a redelivery has taken over). Action keys are
    -- left alone: a Pending one already means "run the node again", and Failed would replay a failure
    -- that never happened.
    DECLARE @Released INT;

    BEGIN TRANSACTION;

    UPDATE dbo.IdempotencyKeys
    SET Status = N'Failed',
        ErrorJson = N'{"error":"abandoned by a stopped engine"}',
        CompletedAt = SYSUTCDATETIME()
    WHERE Status = N'Pending'
      AND (KeyValue LIKE N'inbound-event:%' OR KeyValue LIKE N'trigger-start:%');

    SET @Released = @@ROWCOUNT;

    -- A trigger start that got as far as its run, whether the stopped engine left its claim Pending or
    -- marked it Failed on the way down: the run's root branch was written with it and this leader's
    -- recovery runs it, so the start is settled. Left Failed, the redelivery would reclaim it, find the
    -- run and hand the same branch to the pump a second time. Run after the first update, so a start
    -- an overlapping old leader commits in between is settled rather than left Failed.
    UPDATE ik
    SET Status = N'Succeeded',
        ErrorJson = NULL,
        CompletedAt = SYSUTCDATETIME()
    FROM dbo.IdempotencyKeys ik
    WHERE ik.Status IN (N'Pending', N'Failed')
      AND ik.KeyValue LIKE N'trigger-start:%'
      AND EXISTS (SELECT 1 FROM dbo.Runs r WHERE r.TriggerStartKey = ik.KeyValue);

    COMMIT TRANSACTION;

    SELECT @Released;
END;
GO
