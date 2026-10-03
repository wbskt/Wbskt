-- Deletes a workflow: tombstones its RefId (dbo.WorkflowDeletions), disables every version, and
-- removes its trigger and schedule registrations and any queued trigger events, exactly as
-- Workspace_Retire does for a whole workspace. Version rows and runs stay, so history is readable.
--
-- Scoped by workspace. Returns nothing when the workflow is not in @WorkspaceId or is already
-- deleted; otherwise one row per thing the caller must still act on outside the database:
--   Kind 'run'        - a run still in progress; request its cancellation.
--   Kind 'definition' - a version disabled here; evict it from definition caches.
CREATE PROCEDURE dbo.WorkflowDefinition_Delete
    @RefId       UNIQUEIDENTIFIER,
    @WorkspaceId INT,
    @DeletedBy   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Definitions TABLE (Id INT PRIMARY KEY);

    BEGIN TRANSACTION;

    -- The same lock WorkflowDefinition_Publish takes, so a publish racing the delete either lands
    -- first (and its version is disabled here) or sees the tombstone and is refused.
    INSERT INTO @Definitions (Id)
    SELECT Id
    FROM dbo.WorkflowDefinitions WITH (HOLDLOCK, UPDLOCK)
    WHERE RefId = @RefId AND WorkspaceId = @WorkspaceId;

    IF NOT EXISTS (SELECT 1 FROM @Definitions)
       OR EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WHERE RefId = @RefId)
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.WorkflowDeletions (RefId, WorkspaceId, DeletedBy)
    VALUES (@RefId, @WorkspaceId, @DeletedBy);

    DELETE TR
    FROM dbo.TriggerRegistrations TR
    INNER JOIN @Definitions D ON D.Id = TR.WorkflowDefinitionId;

    DELETE SF
    FROM dbo.ScheduledFires SF
    INNER JOIN @Definitions D ON D.Id = SF.WorkflowDefinitionId;

    DELETE FROM dbo.PendingTriggerEvents
    WHERE WorkflowRefId = @RefId;

    UPDATE WD
    SET IsEnabled = 0
    FROM dbo.WorkflowDefinitions WD
    INNER JOIN @Definitions D ON D.Id = WD.Id
    WHERE WD.IsEnabled = 1;

    COMMIT TRANSACTION;

    SELECT N'run' AS Kind, RU.Id AS Id
    FROM dbo.Runs RU
    INNER JOIN @Definitions D ON D.Id = RU.WorkflowDefinitionId
    WHERE RU.Status IN (N'Running', N'Failing')

    UNION ALL

    SELECT N'definition', CAST(D.Id AS BIGINT)
    FROM @Definitions D;
END
GO
