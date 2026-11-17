-- Where a client trigger with a hold time stands, for the live version of the workflow that owns it
-- (only a live version has registrations). No row when the node has no hold, or no message has reached it yet.
CREATE PROCEDURE dbo.ClientHoldState_GetBy_Trigger
    @WorkflowRefId UNIQUEIDENTIFIER,
    @TriggerNodeId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) CHS.State, CHS.SinceAt, CHS.DueAt, CHS.LastEventAt
    FROM dbo.TriggerRegistrations TR
    INNER JOIN dbo.ClientHoldStates CHS ON CHS.TriggerKey = TR.TriggerKey
    WHERE TR.WorkflowRefId = @WorkflowRefId
      AND TR.TriggerNodeId = @TriggerNodeId
      AND TR.TriggerKind = N'client-hold'
    ORDER BY CHS.UpdatedAt DESC;
END
GO
