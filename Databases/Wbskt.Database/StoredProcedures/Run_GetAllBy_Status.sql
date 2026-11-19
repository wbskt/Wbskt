CREATE PROCEDURE dbo.Run_GetAllBy_Status
    @Status NVARCHAR(32)
AS
BEGIN
    SET NOCOUNT ON;

    -- Crash recovery reads the runs left in 'Failing' or 'Cancelling' by status alone: a run whose stop
    -- was cut short and whose remaining branches are all Waiting has no Active branch for
    -- Branch_GetRunning to find it by. The second predicate repeats IX_Runs_WorkspaceId_Active's
    -- filter word for word, which is what lets the optimizer use that index; without it every run kept
    -- is scanned. Only an in-flight status is ever asked for.
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
        r.CreatedAt,
        r.ParentRunRefId,
        r.OutputJson
    FROM dbo.Runs r
    WHERE r.Status = @Status
      AND r.Status IN (N'Running', N'Failing', N'Cancelling')
    ORDER BY r.Id;
END;
GO
