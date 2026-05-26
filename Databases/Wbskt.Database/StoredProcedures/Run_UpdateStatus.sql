CREATE PROCEDURE dbo.Run_UpdateStatus
    @RefId                  UNIQUEIDENTIFIER,
    @Status                 NVARCHAR(32),
    @CompletedAt            DATETIME2(3),
    @CancellationRequestedAt DATETIME2(3),
    @CancellationReason     NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Runs
    SET Status                  = @Status,
        CompletedAt             = @CompletedAt,
        CancellationRequestedAt = @CancellationRequestedAt,
        CancellationReason      = @CancellationReason
    WHERE RefId = @RefId;

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
