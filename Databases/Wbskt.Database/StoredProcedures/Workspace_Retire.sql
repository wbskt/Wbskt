-- Shuts down everything in this database that belongs to a workspace the auth host has deleted.
-- Workspaces live in the auth database, so nothing here can cascade from them; without this a
-- deleted workspace's policies keep accepting their PIN, its clients keep connecting, and its
-- schedules and webhooks keep starting runs.
--
-- Rows are disabled or revoked rather than deleted, so the history (clients, definitions, runs)
-- stays readable for audit. Only the trigger and schedule registrations are deleted, exactly as
-- deprecating a single workflow does, and the workspace's integrations with their credentials. The
-- clients' readings are queued for clearing.
--
-- Idempotent: the event that drives it can be delivered more than once. A client already revoked is
-- not reported again, so the caller does not re-announce it.
--
-- Returns one row per thing the caller must still act on outside the database:
--   Kind 'client'     - a client revoked by this call; announce it so socket hosts drop the connection.
--   Kind 'run'        - a run still in progress; request its cancellation.
--   Kind 'definition' - a definition disabled here; evict it from definition caches.
CREATE PROCEDURE dbo.Workspace_Retire
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Revoked TABLE (ClientId INT, ClientRefId UNIQUEIDENTIFIER, PolicyId INT);
    DECLARE @Definitions TABLE (Id INT PRIMARY KEY);

    BEGIN TRANSACTION;

    UPDATE dbo.RegistrationPolicies
    SET IsEnabled = 0
    WHERE WorkspaceId = @WorkspaceId
      AND IsEnabled = 1;

    UPDATE dbo.Clients
    SET Status = 2 -- Revoked
    OUTPUT INSERTED.Id, INSERTED.RefId, INSERTED.PolicyId INTO @Revoked
    WHERE WorkspaceId = @WorkspaceId
      AND Status IN (0, 1); -- Pending, Registered; Revoked and Rejected already cannot connect

    INSERT INTO @Definitions (Id)
    SELECT Id FROM dbo.WorkflowDefinitions WHERE WorkspaceId = @WorkspaceId;

    DELETE TR
    FROM dbo.TriggerRegistrations TR
    INNER JOIN @Definitions D ON D.Id = TR.WorkflowDefinitionId;

    DELETE SF
    FROM dbo.ScheduledFires SF
    INNER JOIN @Definitions D ON D.Id = SF.WorkflowDefinitionId;

    -- Events queued behind a busy run would otherwise start one when it finishes.
    DELETE PTE
    FROM dbo.PendingTriggerEvents PTE
    WHERE PTE.WorkflowRefId IN (
        SELECT WD.RefId FROM dbo.WorkflowDefinitions WD INNER JOIN @Definitions D ON D.Id = WD.Id);

    DELETE TDO
    FROM dbo.TriggerDispatchOutcomes TDO
    WHERE TDO.WorkflowRefId IN (
        SELECT WD.RefId FROM dbo.WorkflowDefinitions WD INNER JOIN @Definitions D ON D.Id = WD.Id);

    UPDATE dbo.WorkflowDefinitions
    SET IsEnabled = 0
    WHERE WorkspaceId = @WorkspaceId
      AND IsEnabled = 1;

    -- Integrations are deleted, not disabled: each holds a credential for an outside account, and
    -- nothing about a deleted workspace's history needs it.
    DELETE R
    FROM dbo.WorkflowIntegrationRefs R
    INNER JOIN @Definitions D ON D.Id = R.WorkflowDefinitionId;

    DELETE FROM dbo.Integrations WHERE WorkspaceId = @WorkspaceId;

    DELETE FROM dbo.WorkflowDrafts WHERE WorkspaceId = @WorkspaceId;

    -- Its copy of the tenant's plan is kept and marked: what the workspace spent this month still counts
    -- against the tenant's credits and messages (dbo.TenantWorkspaces), and the retention sweeps still
    -- find its history. Deleting it handed the month's usage back, so deleting a workspace and making
    -- another reset the meters.
    UPDATE dbo.WorkspaceAccounts
    SET RetiredAt = SYSUTCDATETIME()
    WHERE WorkspaceId = @WorkspaceId
      AND RetiredAt IS NULL;

    -- Its clients' readings are cleared outright, in batches (dbo.ClientReadings_Compact), as a deleted
    -- client's are: nobody can read them any more, and the plan's days would keep them for up to two years.
    -- The check locks the key, so a Client_Delete queuing one of them at the same moment waits rather
    -- than failing this whole retire on the primary key.
    INSERT INTO dbo.DeletedClientReadings (ClientId)
    SELECT C.Id
    FROM dbo.Clients C
    WHERE C.WorkspaceId = @WorkspaceId
      AND NOT EXISTS (SELECT 1 FROM dbo.DeletedClientReadings D WITH (UPDLOCK, HOLDLOCK) WHERE D.ClientId = C.Id);

    COMMIT TRANSACTION;

    SELECT N'client' AS Kind, CAST(R.ClientId AS BIGINT) AS Id, R.ClientRefId AS RefId, R.PolicyId, P.RefId AS PolicyRefId
    FROM @Revoked R
    INNER JOIN dbo.RegistrationPolicies P ON P.Id = R.PolicyId

    UNION ALL

    SELECT N'run', CAST(RU.Id AS BIGINT), RU.RefId, NULL, NULL
    FROM dbo.Runs RU
    INNER JOIN @Definitions D ON D.Id = RU.WorkflowDefinitionId
    WHERE RU.Status IN (N'Running', N'Failing')

    UNION ALL

    SELECT N'definition', CAST(D.Id AS BIGINT), NULL, NULL, NULL
    FROM @Definitions D;
END
GO
