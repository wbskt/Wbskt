-- Takes a workflow out of service (@Deprecate = 0: Disabled, still editable) or retires it
-- (@Deprecate = 1: Deprecated, read-only). Either way no version is live afterwards: the live
-- version's trigger registrations and schedules go, and every event queued behind a busy run is
-- dropped, all in one transaction. Runs already going are left alone; the caller may cancel the
-- ones returned.
--
-- Disable is allowed from Enabled, and changes nothing from Disabled; Deprecate is allowed from
-- Enabled and Disabled, and changes nothing from Deprecated. Refuses with 50040 when the workflow
-- is unknown, another workspace's or deleted, and with 50041 when disabling a deprecated workflow.
--
-- Returns the workflow's runs still going (Running or Failing), whichever version they run. A
-- run cannot start after the commit (Run_Start checks the row first, under a shared lock), so the
-- list is complete.
CREATE PROCEDURE dbo.Workflow_Disable
    @RefId                    UNIQUEIDENTIFIER,
    @WorkspaceId              INT,
    @ChangedBy                INT,
    @Deprecate                BIT,
    @Changed                  BIT OUTPUT,
    @PreviousLiveDefinitionId INT OUTPUT,
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

    IF @State = 3 AND @Deprecate = 0
        THROW 50041, 'The workflow is deprecated.', 1;

    DECLARE @Target TINYINT = CASE WHEN @Deprecate = 1 THEN 3 ELSE 2 END;

    IF @State <> @Target
    BEGIN
        DELETE FROM dbo.TriggerRegistrations
         WHERE WorkflowRefId = @RefId;

        DELETE FROM dbo.ScheduledFires
         WHERE WorkflowRefId = @RefId;

        DELETE FROM dbo.PendingTriggerEvents
         WHERE WorkflowRefId = @RefId;

        SET @DroppedQueuedEvents = @@ROWCOUNT;

        UPDATE dbo.Workflows
           SET State = @Target,
               LiveDefinitionId = NULL,
               StateChangedBy = @ChangedBy,
               StateChangedAt = SYSUTCDATETIME()
         WHERE RefId = @RefId;

        SET @Changed = 1;
    END

    COMMIT TRANSACTION;

    -- A test run is the author's own and is left out; it was not started by a trigger.
    SELECT RU.Id
      FROM dbo.Runs RU
     WHERE RU.WorkflowRefId = @RefId
       AND RU.Status IN (N'Running', N'Failing')
       AND RU.IsTest = 0;
END;
GO
