CREATE PROCEDURE dbo.Run_SetTerminal
    @RunId INT,
    @Status NVARCHAR(32),
    @CompletedAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Runs WITH (ROWLOCK)
    SET Status = @Status,
        CompletedAt = @CompletedAt
    WHERE Id = @RunId;

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
    WHERE Id = @RunId;
END;
GO
