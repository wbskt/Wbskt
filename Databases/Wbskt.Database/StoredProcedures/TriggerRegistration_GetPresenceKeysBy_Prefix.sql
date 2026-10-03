-- The presence triggers a client's connect or disconnect should park a check for. Scoped to the
-- client's own workspace: a workflow cannot watch another workspace's device by knowing its id.
CREATE PROCEDURE dbo.TriggerRegistration_GetPresenceKeysBy_Prefix
    @KeyPrefix   NVARCHAR(200),
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DISTINCT TR.TriggerKey
    FROM dbo.TriggerRegistrations TR
    INNER JOIN dbo.WorkflowDefinitions WD ON WD.Id = TR.WorkflowDefinitionId
    WHERE TR.TriggerKind = N'presence'
      AND TR.TriggerKey LIKE @KeyPrefix + N'%'
      AND WD.WorkspaceId = @WorkspaceId
      AND WD.IsEnabled = 1;
END;
GO
