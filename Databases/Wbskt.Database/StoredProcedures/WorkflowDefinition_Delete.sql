-- Deletes a workflow: sets its dbo.Workflows row to Deleted (State 4) with no live version, and
-- removes its trigger and schedule registrations, any queued trigger events and the triggers'
-- recent outcomes, exactly as Workspace_Retire does for a whole workspace. Version rows and runs
-- stay, so history is readable.
--
-- Scoped by workspace. Returns nothing when the workflow is not in @WorkspaceId or is already
-- deleted; otherwise one row per thing the caller must still act on outside the database:
--   Kind 'run'        - a run still in progress; request its cancellation.
--   Kind 'definition' - a version of the workflow; evict it from definition caches.
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

    -- The draft's row first, the order WorkflowDraft_Save takes it in, so a save racing the delete
    -- waits for it instead of deadlocking on the workflow row taken below.
    DECLARE @LockedDraftRevision INT;
    SELECT @LockedDraftRevision = Revision
      FROM dbo.WorkflowDrafts WITH (UPDLOCK, HOLDLOCK)
     WHERE RefId = @RefId;

    -- Then the workflow's row, the lock WorkflowDefinition_Publish and Workflow_Enable take, so a
    -- publish or enable racing the delete either lands first or sees Deleted and is refused.
    DECLARE @State TINYINT;
    SELECT @State = State
      FROM dbo.Workflows WITH (UPDLOCK, HOLDLOCK)
     WHERE RefId = @RefId
       AND WorkspaceId = @WorkspaceId;

    IF @State IS NULL OR @State = 4
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    INSERT INTO @Definitions (Id)
    SELECT Id
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId AND WorkspaceId = @WorkspaceId;

    UPDATE dbo.Workflows
       SET State = 4,
           LiveDefinitionId = NULL,
           StateChangedBy = @DeletedBy,
           StateChangedAt = SYSUTCDATETIME()
     WHERE RefId = @RefId;

    DELETE TR
    FROM dbo.TriggerRegistrations TR
    INNER JOIN @Definitions D ON D.Id = TR.WorkflowDefinitionId;

    DELETE SF
    FROM dbo.ScheduledFires SF
    INNER JOIN @Definitions D ON D.Id = SF.WorkflowDefinitionId;

    DELETE FROM dbo.PendingTriggerEvents
    WHERE WorkflowRefId = @RefId;

    DELETE FROM dbo.TriggerDispatchOutcomes
    WHERE WorkflowRefId = @RefId;

    DELETE FROM dbo.WorkflowDrafts
    WHERE RefId = @RefId;

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
