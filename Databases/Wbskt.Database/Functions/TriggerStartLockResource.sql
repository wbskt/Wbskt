-- The applock name for one workflow, trigger node and correlation key (#467, #470). Run_Start,
-- PendingTriggerEvent_Enqueue and PendingTriggerEvent_DequeueNext all take it, so starting a run,
-- queueing behind one and draining the queue take turns per key. Hashed because a key can be 400
-- characters and sp_getapplock takes 255; upper-cased because the procs compare keys under the
-- case-insensitive collation, so "Dev-1" and "dev-1" are one key and must take one lock.
CREATE FUNCTION dbo.TriggerStartLockResource
(
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400)
)
RETURNS NVARCHAR(255)
WITH SCHEMABINDING
AS
BEGIN
    RETURN CONCAT(N'trigger-start:', @WorkflowRefId, N':', @TriggerNodeId, N':',
        CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', UPPER(@CorrelationKey)), 2));
END;
GO
