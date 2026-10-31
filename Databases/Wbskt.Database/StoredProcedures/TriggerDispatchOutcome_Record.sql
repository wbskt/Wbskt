-- Records one trigger outcome and trims the trigger's rows to the newest @Keep. Two concurrent
-- records can briefly leave @Keep + 1 rows; the next record trims them, so the bound holds.
CREATE PROCEDURE dbo.TriggerDispatchOutcome_Record
    @WorkflowRefId  UNIQUEIDENTIFIER,
    @TriggerNodeId  UNIQUEIDENTIFIER,
    @OccurredAt     DATETIME2(3),
    @ChannelKind    NVARCHAR(64),
    @MessageType    NVARCHAR(200),
    @Outcome        NVARCHAR(32),
    @CorrelationKey NVARCHAR(400),
    @RunRefId       UNIQUEIDENTIFIER,
    @Keep           INT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.TriggerDispatchOutcomes (WorkflowRefId, TriggerNodeId, OccurredAt, ChannelKind, MessageType, Outcome, CorrelationKey, RunRefId)
    VALUES (@WorkflowRefId, @TriggerNodeId, @OccurredAt, @ChannelKind, @MessageType, @Outcome, @CorrelationKey, @RunRefId);

    DELETE FROM dbo.TriggerDispatchOutcomes
    WHERE WorkflowRefId = @WorkflowRefId
      AND TriggerNodeId = @TriggerNodeId
      AND Id < (SELECT MIN(Kept.Id)
                FROM (SELECT TOP (@Keep) Id
                      FROM dbo.TriggerDispatchOutcomes
                      WHERE WorkflowRefId = @WorkflowRefId AND TriggerNodeId = @TriggerNodeId
                      ORDER BY Id DESC) AS Kept);
END
GO
