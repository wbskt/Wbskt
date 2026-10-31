CREATE PROCEDURE dbo.PendingTriggerEvent_Enqueue
    @WorkflowRefId    UNIQUEIDENTIFIER,
    @TriggerNodeId    UNIQUEIDENTIFIER,
    @CorrelationKey   NVARCHAR(400),
    @InboundEventJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NewId BIGINT;

    BEGIN TRANSACTION;

    -- The Queue policy saw an active run and queues behind it, but the run may have finished since,
    -- and its finalizer may already have drained the queue and found it empty (#470). The run is
    -- looked for again under the key's lock, which DequeueNext takes as well: either the drain comes
    -- after this insert and finds it, or the finalizer's terminal write came first and this returns
    -- no row, telling the caller to start a run instead.
    DECLARE @Resource NVARCHAR(255) = dbo.TriggerStartLockResource(@WorkflowRefId, @TriggerNodeId, @CorrelationKey);
    DECLARE @LockResult INT;
    EXEC @LockResult = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
    IF @LockResult < 0
        THROW 50029, 'Timed out waiting for the trigger start lock.', 1;

    -- IX_Runs_WorkflowRefId_CorrelationKey_Active's filter word for word, so the index is used.
    IF NOT EXISTS (SELECT 1
                   FROM dbo.Runs
                   WHERE WorkflowRefId = @WorkflowRefId
                     AND TriggerNodeId = @TriggerNodeId
                     AND CorrelationKey = @CorrelationKey
                     AND Status IN (N'Running', N'Failing', N'Cancelling'))
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.PendingTriggerEvents
        (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
    VALUES
        (@WorkflowRefId, @TriggerNodeId, @CorrelationKey, @InboundEventJson);

    SET @NewId = SCOPE_IDENTITY();

    COMMIT TRANSACTION;

    SELECT
        Id,
        WorkflowRefId,
        TriggerNodeId,
        CorrelationKey,
        InboundEventJson,
        EnqueuedAt,
        CreatedAt
    FROM dbo.PendingTriggerEvents
    WHERE Id = @NewId;
END;
GO
