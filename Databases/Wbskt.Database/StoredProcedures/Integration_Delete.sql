-- Deletes an integration unless a workflow's live version sends through it. The row is locked
-- first, and WorkflowDefinition_Publish and Workflow_Enable take the same lock on every integration
-- they reference, so neither can add a live use between the check and the delete.
--
-- Returns the blocking uses (live versions) when it refused, and no rows when it deleted (or there
-- was nothing left to delete). A use in a version that is not live does not block: its refs go with
-- the row, and enabling that version is refused because the integration it names is gone.
--
-- Runs still in flight on a version that is no longer live (or of a workflow just disabled) would
-- fail at the step with INTEGRATION_NOT_FOUND. Unless @AllowActiveRuns is set, the delete is refused
-- while any is, and returns those steps with ActiveRuns, how many runs are on each.
CREATE PROCEDURE dbo.Integration_Delete
    @Id              INT,
    @WorkspaceId     INT,
    -- 1 by default so the previous image deletes as it did; the default goes in the release after (#1059).
    @AllowActiveRuns BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;

    IF NOT EXISTS (SELECT 1 FROM dbo.Integrations WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND WorkspaceId = @WorkspaceId)
    BEGIN
        COMMIT TRAN;
        RETURN;
    END

    IF EXISTS (SELECT 1 FROM dbo.IntegrationUsages WHERE IntegrationId = @Id AND IsLive = 1)
    BEGIN
        COMMIT TRAN;

        SELECT
            WorkflowRefId,
            WorkflowName,
            NodeId,
            NodeName,
            IsLive,
            0 AS ActiveRuns
        FROM dbo.IntegrationUsages
        WHERE IntegrationId = @Id AND IsLive = 1
        ORDER BY WorkflowName, NodeName;
        RETURN;
    END

    IF @AllowActiveRuns = 0
       AND EXISTS (SELECT 1
                   FROM dbo.WorkflowIntegrationRefs R
                   INNER JOIN dbo.Runs Ru ON Ru.WorkflowDefinitionId = R.WorkflowDefinitionId
                   WHERE R.IntegrationId = @Id
                     AND Ru.Status IN (N'Running', N'Failing', N'Cancelling'))
    BEGIN
        COMMIT TRAN;

        SELECT
            WD.RefId    AS WorkflowRefId,
            WD.Name     AS WorkflowName,
            R.NodeId,
            R.NodeName,
            CAST(0 AS BIT) AS IsLive,
            COUNT(*)    AS ActiveRuns
        FROM dbo.WorkflowIntegrationRefs R
        INNER JOIN dbo.WorkflowDefinitions WD ON WD.Id = R.WorkflowDefinitionId
        INNER JOIN dbo.Runs Ru ON Ru.WorkflowDefinitionId = R.WorkflowDefinitionId
        WHERE R.IntegrationId = @Id
          AND Ru.Status IN (N'Running', N'Failing', N'Cancelling')
        GROUP BY WD.RefId, WD.Name, R.NodeId, R.NodeName
        ORDER BY WD.Name, R.NodeName;
        RETURN;
    END

    DELETE FROM dbo.WorkflowIntegrationRefs WHERE IntegrationId = @Id;
    DELETE FROM dbo.Integrations WHERE Id = @Id AND WorkspaceId = @WorkspaceId;

    COMMIT TRAN;
END
GO
