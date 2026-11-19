CREATE PROCEDURE dbo.PendingTriggerEvent_LeaseNext
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400),
    @LeaseSec       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    -- Under the key's lock, so an Enqueue that is still deciding (it saw the run active) commits its
    -- row before this looks, rather than just after this found the queue empty (#470).
    DECLARE @Resource NVARCHAR(255) = dbo.TriggerStartLockResource(@WorkflowRefId, @TriggerNodeId, @CorrelationKey);
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 50029, 'Timed out waiting for the trigger start lock.', 1;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    -- Leased, not deleted: the row goes only once its replay has been handed over
    -- (PendingTriggerEvent_DeleteById), so a replay whose start fails is tried again once the lease
    -- runs out (#962). Only the head is looked at: one still leased holds its place, and the events
    -- behind it are not started out of order.
    WITH Head AS (
        SELECT TOP (1)
            Id,
            WorkflowRefId,
            TriggerNodeId,
            CorrelationKey,
            InboundEventJson,
            EnqueuedAt,
            LeasedUntil
        FROM dbo.PendingTriggerEvents WITH (ROWLOCK, UPDLOCK)
        WHERE WorkflowRefId = @WorkflowRefId
          AND TriggerNodeId = @TriggerNodeId
          AND CorrelationKey = @CorrelationKey
        ORDER BY EnqueuedAt ASC, Id ASC
    )
    UPDATE Head
    SET LeasedUntil = DATEADD(SECOND, @LeaseSec, @Now)
    OUTPUT
        inserted.Id,
        inserted.WorkflowRefId,
        inserted.TriggerNodeId,
        inserted.CorrelationKey,
        inserted.InboundEventJson,
        inserted.EnqueuedAt
    WHERE LeasedUntil IS NULL
       OR LeasedUntil < @Now;

    COMMIT TRANSACTION;
END;
GO
