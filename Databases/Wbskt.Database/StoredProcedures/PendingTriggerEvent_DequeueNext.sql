CREATE PROCEDURE dbo.PendingTriggerEvent_DequeueNext
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

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
END;
GO
