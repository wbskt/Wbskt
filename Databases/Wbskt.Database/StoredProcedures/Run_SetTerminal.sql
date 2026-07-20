CREATE PROCEDURE dbo.Run_SetTerminal
    @RunId INT,
    @Status NVARCHAR(32),
    @CompletedAt DATETIME2(3)
AS
BEGIN
    SET NOCOUNT ON;

    -- Only a non-terminal run can be transitioned. This makes finalization idempotent:
    -- a run already Faulted (or finalized by a concurrent caller) is left untouched, and
    -- the caller learns via @Transitioned whether it actually performed the transition.
    UPDATE dbo.Runs WITH (ROWLOCK)
    SET Status = @Status,
        CompletedAt = @CompletedAt
    WHERE Id = @RunId
      AND Status IN (N'Running', N'Failing', N'Cancelling');

    DECLARE @Transitioned BIT = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

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
        CreatedAt,
        @Transitioned AS Transitioned
    FROM dbo.Runs
    WHERE Id = @RunId;
END;
GO
