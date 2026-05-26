CREATE PROCEDURE dbo.Run_GetActiveBy_WorkflowRefId_CorrelationKey
    @WorkflowRefId UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        RefId,
        WorkflowDefinitionId,
        WorkflowRefId,
        WorkflowVersion,
        TriggerNodeId,
        CorrelationKey,
        Status,
        StartedAt,
        CompletedAt,
        CancellationRequestedAt,
        CancellationReason,
        CreditBudget,
        CreatedAt
    FROM dbo.Runs
    WHERE WorkflowRefId = @WorkflowRefId
      AND CorrelationKey = @CorrelationKey
      AND (Status = N'Running' OR Status = N'Failing' OR Status = N'Cancelling');
END;
GO
