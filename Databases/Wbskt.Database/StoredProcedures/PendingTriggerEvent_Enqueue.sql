CREATE PROCEDURE dbo.PendingTriggerEvent_Enqueue
    @WorkflowRefId    UNIQUEIDENTIFIER,
    @TriggerNodeId    UNIQUEIDENTIFIER,
    @CorrelationKey   NVARCHAR(400),
    @InboundEventJson NVARCHAR(MAX),

    -- A replay's own row (PendingTriggerEventDrainer), which is not an event to queue behind. Defaulted
    -- because the previous engine image doesn't pass it; required in the release after (#1059).
    @ExceptId         BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @NewId BIGINT;

    BEGIN TRANSACTION;

    -- The Queue policy queues an event behind an active run, and also behind events still waiting on
    -- the key with no run going (a replay whose start failed holds its lease for a while), so a new
    -- event never starts ahead of them; the finalizer or PendingTriggerEventBacklogReaper starts the
    -- head. Both are looked for under the key's lock, which LeaseNext takes as well: either the drain
    -- comes after this insert and finds it, or the finalizer's terminal write came first and emptied
    -- the queue, and this returns no row, telling the caller to start a run instead (#470).
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
       AND NOT EXISTS (SELECT 1
                       FROM dbo.PendingTriggerEvents
                       WHERE WorkflowRefId = @WorkflowRefId
                         AND TriggerNodeId = @TriggerNodeId
                         AND CorrelationKey = @CorrelationKey
                         AND (@ExceptId IS NULL OR Id <> @ExceptId))
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
        EnqueuedAt
    FROM dbo.PendingTriggerEvents
    WHERE Id = @NewId;
END;
GO
