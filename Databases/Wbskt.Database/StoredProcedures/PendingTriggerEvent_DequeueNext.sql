CREATE PROCEDURE dbo.PendingTriggerEvent_DequeueNext
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400)
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

    WITH Dequeue AS (
        SELECT TOP (1)
            Id,
            WorkflowRefId,
            TriggerNodeId,
            CorrelationKey,
            InboundEventJson,
            EnqueuedAt,
            CreatedAt
        FROM dbo.PendingTriggerEvents WITH (ROWLOCK, UPDLOCK, READPAST)
        WHERE WorkflowRefId = @WorkflowRefId
          AND TriggerNodeId = @TriggerNodeId
          AND CorrelationKey = @CorrelationKey
        ORDER BY EnqueuedAt ASC
    )
    DELETE FROM Dequeue
    OUTPUT
        deleted.Id,
        deleted.WorkflowRefId,
        deleted.TriggerNodeId,
        deleted.CorrelationKey,
        deleted.InboundEventJson,
        deleted.EnqueuedAt,
        deleted.CreatedAt;

    COMMIT TRANSACTION;
END;
GO
