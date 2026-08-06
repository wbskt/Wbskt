CREATE PROCEDURE dbo.TriggerRegistration_GetAllBy_WorkflowDefinitionId
    @WorkflowDefinitionId INT
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
        WebhookSecret,
        CreatedAt
    FROM dbo.TriggerRegistrations
    WHERE WorkflowDefinitionId = @WorkflowDefinitionId;
END;
GO
