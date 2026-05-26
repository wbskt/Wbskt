CREATE PROCEDURE dbo.TriggerRegistration_GetBy_TriggerKey
    @TriggerKey NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        WorkflowDefinitionId,
        WorkflowRefId,
        WorkflowVersion,
        TriggerNodeId,
        TriggerKind,
        TriggerKey,
        CorrelationExpression,
        ConcurrencyPolicy,
        FilterExpression,
        CreatedAt
    FROM dbo.TriggerRegistrations
    WHERE TriggerKey = @TriggerKey;
END;
GO
