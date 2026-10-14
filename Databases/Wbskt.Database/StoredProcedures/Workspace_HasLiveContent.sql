-- Whether dbo.Workspace_Retire would still find something to shut down for a workspace: an enabled
-- registration policy, a client that can still connect, an enabled workflow, an integration, a
-- trigger or schedule registration, an event queued behind a run, or a run still in progress.
--
-- The management host's deleted workspace sweep asks this for each workspace the auth host reports
-- deleted, and retires only those it answers 1 for. A workspace the WorkspaceDeletedEvent already
-- retired answers 0, so the sweep's "retired" count is the repairs it made, not every delete it saw.
-- A run still in progress counts: retiring again sends its cancel again.
CREATE PROCEDURE dbo.Workspace_HasLiveContent
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN
           EXISTS (SELECT 1 FROM dbo.RegistrationPolicies WHERE WorkspaceId = @WorkspaceId AND IsEnabled = 1)
        OR EXISTS (SELECT 1 FROM dbo.Clients WHERE WorkspaceId = @WorkspaceId AND Status IN (0, 1)) -- Pending, Registered
        OR EXISTS (SELECT 1 FROM dbo.WorkflowDefinitions WHERE WorkspaceId = @WorkspaceId AND IsEnabled = 1)
        OR EXISTS (SELECT 1 FROM dbo.Integrations WHERE WorkspaceId = @WorkspaceId)
        OR EXISTS (
            SELECT 1
            FROM dbo.WorkflowDefinitions WD
            WHERE WD.WorkspaceId = @WorkspaceId
              AND (   EXISTS (SELECT 1 FROM dbo.TriggerRegistrations TR WHERE TR.WorkflowDefinitionId = WD.Id)
                   OR EXISTS (SELECT 1 FROM dbo.ScheduledFires SF WHERE SF.WorkflowDefinitionId = WD.Id)
                   OR EXISTS (SELECT 1 FROM dbo.PendingTriggerEvents PTE WHERE PTE.WorkflowRefId = WD.RefId)
                   OR EXISTS (SELECT 1 FROM dbo.Runs RU WHERE RU.WorkflowDefinitionId = WD.Id AND RU.Status IN (N'Running', N'Failing'))))
        THEN 1 ELSE 0 END AS BIT) AS HasLiveContent;
END
GO
