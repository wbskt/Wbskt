CREATE PROCEDURE dbo.Run_ListBy_WorkflowRefId
    @WorkflowRefId UNIQUEIDENTIFIER,
    @StatusFilter NVARCHAR(40) = NULL,
    @Top INT = 50,
    @CursorId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
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
      AND (@StatusFilter IS NULL OR Status = @StatusFilter)
      AND (@CursorId IS NULL OR Id < @CursorId)
    ORDER BY Id DESC;
END;
GO
