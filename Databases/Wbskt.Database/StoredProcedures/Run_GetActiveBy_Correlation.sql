CREATE PROCEDURE dbo.Run_GetActiveBy_Correlation
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
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
      AND TriggerNodeId = @TriggerNodeId
      AND CorrelationKey = @CorrelationKey
      AND Status IN (N'Running', N'Cancelling', N'Failing');
END;
GO
