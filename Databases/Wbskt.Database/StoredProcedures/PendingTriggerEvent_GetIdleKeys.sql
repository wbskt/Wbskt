-- Queues nothing will drain: no run is going on the key, so no finalizer will release the next event.
-- That is left behind when the engine stops between a run's end and its drain, or when a replay's
-- start failed and its lease has run out.
CREATE PROCEDURE dbo.PendingTriggerEvent_GetIdleKeys
    @BatchSize INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Now DATETIME2(3) = SYSUTCDATETIME();

    SELECT DISTINCT TOP (@BatchSize)
        p.WorkflowRefId,
        p.TriggerNodeId,
        p.CorrelationKey
    FROM dbo.PendingTriggerEvents AS p
    WHERE (p.LeasedUntil IS NULL OR p.LeasedUntil < @Now)
      -- IX_Runs_WorkflowRefId_CorrelationKey_Active's filter word for word, so the index is used.
      AND NOT EXISTS (SELECT 1
                      FROM dbo.Runs AS r
                      WHERE r.WorkflowRefId = p.WorkflowRefId
                        AND r.TriggerNodeId = p.TriggerNodeId
                        AND r.CorrelationKey = p.CorrelationKey
                        AND r.Status IN (N'Running', N'Failing', N'Cancelling'));
END;
GO
