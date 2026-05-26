CREATE PROCEDURE dbo.Run_GetActiveBy_Correlation
    @WorkflowDefinitionId INT,
    @CorrelationKey       NVARCHAR(400)
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
    WHERE WorkflowDefinitionId = @WorkflowDefinitionId
      AND CorrelationKey = @CorrelationKey
      AND Status IN (N'Running', N'Cancelling', N'Failing');
END;
GO
