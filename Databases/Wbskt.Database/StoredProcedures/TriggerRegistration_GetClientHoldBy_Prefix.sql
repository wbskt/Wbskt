-- The hold triggers a client message should be recorded against: those for its message type and the
-- any-type ones. Compared with LEFT rather than LIKE because the message type is author-chosen and
-- may contain LIKE wildcards ('_' is common). Scoped to the client's own workspace, as presence is.
CREATE PROCEDURE dbo.TriggerRegistration_GetClientHoldBy_Prefix
    @TypePrefix     NVARCHAR(400),
    @WildcardPrefix NVARCHAR(400),
    @WorkspaceId    INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        TR.Id,
        TR.WorkflowDefinitionId,
        TR.WorkflowRefId,
        TR.WorkflowVersion,
        TR.TriggerNodeId,
        TR.TriggerKind,
        TR.TriggerKey,
        TR.CorrelationExpression,
        TR.ConcurrencyPolicy,
        TR.FilterExpression,
        TR.CreatedAt
    FROM dbo.TriggerRegistrations TR
    INNER JOIN dbo.WorkflowDefinitions WD ON WD.Id = TR.WorkflowDefinitionId
    WHERE TR.TriggerKind = N'client-hold'
      AND (LEFT(TR.TriggerKey, LEN(@TypePrefix)) = @TypePrefix
           OR LEFT(TR.TriggerKey, LEN(@WildcardPrefix)) = @WildcardPrefix)
      AND WD.WorkspaceId = @WorkspaceId
      AND WD.IsEnabled = 1;
END;
GO
