CREATE PROCEDURE dbo.PendingTriggerEvent_DequeueNextBy_WorkflowDefinitionId_Correlation
    @WorkflowDefinitionId INT,
    @CorrelationKey       NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

    WITH Dequeue AS (
        SELECT TOP (1)
            p.Id,
            p.WorkflowRefId,
            p.TriggerNodeId,
            p.CorrelationKey,
            p.InboundEventJson,
            p.EnqueuedAt,
            p.CreatedAt
        FROM dbo.PendingTriggerEvents AS p WITH (ROWLOCK, UPDLOCK, READPAST)
        INNER JOIN dbo.WorkflowDefinitions AS w
            ON w.RefId = p.WorkflowRefId
        WHERE w.Id = @WorkflowDefinitionId
          AND p.CorrelationKey = @CorrelationKey
        ORDER BY p.EnqueuedAt ASC
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
