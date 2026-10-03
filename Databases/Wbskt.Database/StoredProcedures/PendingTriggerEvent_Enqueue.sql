CREATE PROCEDURE dbo.PendingTriggerEvent_Enqueue
    @WorkflowRefId    UNIQUEIDENTIFIER,
    @TriggerNodeId    UNIQUEIDENTIFIER,
    @CorrelationKey   NVARCHAR(400),
    @InboundEventJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.PendingTriggerEvents
        (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
    VALUES
        (@WorkflowRefId, @TriggerNodeId, @CorrelationKey, @InboundEventJson);

    DECLARE @NewId BIGINT = SCOPE_IDENTITY();

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
