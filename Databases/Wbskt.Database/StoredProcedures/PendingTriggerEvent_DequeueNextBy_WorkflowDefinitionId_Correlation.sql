CREATE PROCEDURE dbo.PendingTriggerEvent_DequeueNextBy_WorkflowDefinitionId_Correlation
    @WorkflowDefinitionId INT,
    @CorrelationKey       NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

    -- Resolve the definition's RefId up front so the deletable CTE targets a single
    -- base table. Deleting through a CTE that joins WorkflowDefinitions fails with
    -- error 4405 ('modification affects multiple base tables').
    DECLARE @WorkflowRefId UNIQUEIDENTIFIER =
    (
        SELECT RefId
        FROM dbo.WorkflowDefinitions
        WHERE Id = @WorkflowDefinitionId
    );

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
        WHERE p.WorkflowRefId = @WorkflowRefId
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
