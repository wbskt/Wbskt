-- Makes one published version of a workflow the live one, from Enabled (a switch) or Disabled.
-- Everything that decides what starts a run moves in this one transaction: the previous live
-- version's trigger registrations and schedules go, @Registrations and @Schedules (planned from the
-- new version by ITriggerPlanner) come in, events queued for a trigger node the new version does
-- not have are dropped, and the row points at the new version. There is never a moment with two
-- live versions, or with a live version that has no registrations.
--
-- Refuses with 50040 when the workflow is unknown, another workspace's or deleted; 50041 when it is
-- deprecated; 50043 when it has no such version; 50023 when an integration one of its steps sends
-- through (@IntegrationRefs) is no longer the workspace's. Enabling the version that is already live changes
-- nothing (@Changed = 0).
CREATE PROCEDURE dbo.Workflow_Enable
    @RefId                    UNIQUEIDENTIFIER,
    @WorkspaceId              INT,
    @Version                  INT,
    @ChangedBy                INT,
    @Registrations            dbo.TriggerPlanRegistrationTableType READONLY,
    @Schedules                dbo.TriggerPlanScheduleTableType READONLY,
    @IntegrationRefs          dbo.IntegrationRefTableType READONLY,
    @Changed                  BIT OUTPUT,
    @PreviousLiveDefinitionId INT OUTPUT,
    @LiveDefinitionId         INT OUTPUT,
    @DroppedQueuedEvents      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Changed = 0;
    SET @DroppedQueuedEvents = 0;

    BEGIN TRANSACTION;

    DECLARE @State TINYINT;
    SELECT @State = State, @PreviousLiveDefinitionId = LiveDefinitionId
      FROM dbo.Workflows WITH (UPDLOCK, HOLDLOCK)
     WHERE RefId = @RefId
       AND WorkspaceId = @WorkspaceId;

    IF @State IS NULL OR @State = 4
        THROW 50040, 'The workflow was not found.', 1;

    IF @State = 3
        THROW 50041, 'The workflow is deprecated.', 1;

    SELECT @LiveDefinitionId = Id
      FROM dbo.WorkflowDefinitions
     WHERE RefId = @RefId
       AND Version = @Version
       AND Kind = N'Version';

    IF @LiveDefinitionId IS NULL
        THROW 50043, 'The workflow has no such version.', 1;

    -- The integrations its steps send through must still be the workspace's, and stay so until the
    -- switch commits: the update lock here is the one Integration_Delete takes, so a delete either
    -- finished first (and this refuses) or waits and then sees this version live (and refuses).
    IF (SELECT COUNT(*)
          FROM @IntegrationRefs R
         INNER JOIN dbo.Integrations I WITH (UPDLOCK, HOLDLOCK)
            ON I.RefId = R.IntegrationRefId AND I.WorkspaceId = @WorkspaceId) <> (SELECT COUNT(*) FROM @IntegrationRefs)
        THROW 50023, 'A step names an integration the workspace does not have.', 1;

    IF @State = 1 AND @PreviousLiveDefinitionId = @LiveDefinitionId
    BEGIN
        COMMIT TRANSACTION;
        RETURN;
    END

    -- Cleared by workflow rather than by version, so nothing another version left behind survives
    -- and a version that was live before is never registered twice.
    DELETE FROM dbo.TriggerRegistrations
     WHERE WorkflowRefId = @RefId;

    DELETE FROM dbo.ScheduledFires
     WHERE WorkflowRefId = @RefId;

    INSERT INTO dbo.ScheduledFires (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
    SELECT TriggerNodeId, @LiveDefinitionId, @RefId, CronOrInterval, NextFireAt
      FROM @Schedules;

    INSERT INTO dbo.TriggerRegistrations
        (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey, CorrelationExpression, ConcurrencyPolicy, FilterExpression, WebhookSecret, WebhookSecretHash)
    SELECT @LiveDefinitionId, @RefId, @Version, TriggerNodeId, TriggerKind, TriggerKey, CorrelationExpression, ConcurrencyPolicy, FilterExpression, WebhookSecret, WebhookSecretHash
      FROM @Registrations;

    -- Node ids carry across versions, so an event queued for a trigger the new version still has
    -- starts it when its turn comes; one for a trigger the new version dropped has nowhere to go.
    DELETE PTE
      FROM dbo.PendingTriggerEvents PTE
     WHERE PTE.WorkflowRefId = @RefId
       AND NOT EXISTS (SELECT 1 FROM @Registrations R WHERE R.TriggerNodeId = PTE.TriggerNodeId);

    SET @DroppedQueuedEvents = @@ROWCOUNT;

    UPDATE dbo.Workflows
       SET State = 1,
           LiveDefinitionId = @LiveDefinitionId,
           StateChangedBy = @ChangedBy,
           StateChangedAt = SYSUTCDATETIME()
     WHERE RefId = @RefId;

    SET @Changed = 1;

    COMMIT TRANSACTION;
END;
GO
