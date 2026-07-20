CREATE PROCEDURE dbo.Run_UpdateStatus
    @RefId                  UNIQUEIDENTIFIER,
    @Status                 NVARCHAR(32),
    @CompletedAt            DATETIME2(3),
    @CancellationRequestedAt DATETIME2(3),
    @CancellationReason     NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    -- Guard against overwriting a terminal run (e.g. Faulted) with a stale status transition.
    UPDATE dbo.Runs WITH (ROWLOCK)
    SET Status                  = @Status,
        CompletedAt             = COALESCE(@CompletedAt, CompletedAt),
        CancellationRequestedAt = COALESCE(@CancellationRequestedAt, CancellationRequestedAt),
        CancellationReason      = COALESCE(@CancellationReason, CancellationReason)
    WHERE RefId = @RefId
      AND Status IN (N'Running', N'Failing', N'Cancelling');

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
