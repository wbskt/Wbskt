CREATE PROCEDURE dbo.Run_GetAllBy_Status
    @Status NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    -- Crash recovery reads the runs left in 'Failing' or 'Cancelling' by status alone: a run whose stop
    -- was cut short and whose remaining branches are all Waiting has no Active branch for
    -- Branch_GetRunning to find it by. The filtered IX_Runs_WorkflowDefinitionId_Active covers these.
    SELECT
        r.Id,
        r.RefId,
        r.WorkflowDefinitionId,
        r.WorkflowRefId,
        r.WorkflowVersion,
        r.TriggerNodeId,
        r.CorrelationKey,
        r.Status,
        r.StartedAt,
        r.CompletedAt,
        r.CancellationRequestedAt,
        r.CancellationReason,
        r.CreditBudget,
        r.CreatedAt
    FROM dbo.Runs r
    WHERE r.Status = @Status
    ORDER BY r.Id;
END;
GO
