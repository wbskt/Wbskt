-- Records a batch of trigger outcomes, in order, and trims each trigger it touched to its newest
-- @Keep rows. The engine buffers outcomes and writes them every few seconds, so a chatty client costs
-- one call per flush rather than two statements per message per trigger. Two concurrent batches can
-- briefly leave more than @Keep rows; the next batch for that trigger trims them.
CREATE PROCEDURE dbo.TriggerDispatchOutcome_RecordMany
    @Outcomes dbo.TriggerDispatchOutcomeTableType READONLY,
    @Keep     INT
AS
BEGIN
    SET NOCOUNT ON;

    -- Identity values follow the ORDER BY, so the newest outcome gets the highest Id.
    INSERT INTO dbo.TriggerDispatchOutcomes (WorkflowRefId, TriggerNodeId, OccurredAt, ChannelKind, MessageType, Outcome, CorrelationKey, RunRefId)
    SELECT WorkflowRefId, TriggerNodeId, OccurredAt, ChannelKind, MessageType, Outcome, CorrelationKey, RunRefId
    FROM @Outcomes
    ORDER BY Ordinal;

    WITH Ranked AS
    (
        SELECT ROW_NUMBER() OVER (PARTITION BY o.WorkflowRefId, o.TriggerNodeId ORDER BY o.Id DESC) AS Position
        FROM dbo.TriggerDispatchOutcomes o
        WHERE EXISTS (SELECT 1 FROM @Outcomes t WHERE t.WorkflowRefId = o.WorkflowRefId AND t.TriggerNodeId = o.TriggerNodeId)
    )
    DELETE FROM Ranked
    WHERE Position > @Keep;
END
GO
