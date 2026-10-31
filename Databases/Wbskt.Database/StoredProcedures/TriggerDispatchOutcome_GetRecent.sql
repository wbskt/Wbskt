-- The newest outcomes recorded for one trigger node, newest first.
CREATE PROCEDURE dbo.TriggerDispatchOutcome_GetRecent
    @WorkflowRefId UNIQUEIDENTIFIER,
    @TriggerNodeId UNIQUEIDENTIFIER,
    @Take          INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Take) OccurredAt, ChannelKind, MessageType, Outcome, CorrelationKey, RunRefId
    FROM dbo.TriggerDispatchOutcomes
    WHERE WorkflowRefId = @WorkflowRefId
      AND TriggerNodeId = @TriggerNodeId
    ORDER BY Id DESC;
END
GO
