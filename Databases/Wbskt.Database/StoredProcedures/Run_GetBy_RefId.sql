CREATE PROCEDURE dbo.Run_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
    WHERE RefId = @RefId;
END;
GO
