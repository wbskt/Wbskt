CREATE PROCEDURE dbo.PendingTriggerEvent_DeleteAllBy_RunKey
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @CorrelationKey NVARCHAR(400)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.PendingTriggerEvents
    WHERE WorkflowRefId = @WorkflowRefId
      AND TriggerNodeId = @TriggerNodeId
      AND CorrelationKey = @CorrelationKey;
END;
GO
