-- Deletes an integration unless a workflow's live version sends through it. The row is locked
-- first, and WorkflowDefinition_Publish and Workflow_Enable take the same lock on every integration
-- they reference, so neither can add a live use between the check and the delete.
--
-- Returns the blocking uses (live versions) when it refused, and no rows when it deleted (or there
-- was nothing left to delete). A use in a version that is not live does not block: its refs go with
-- the row, and enabling that version is refused because the integration it names is gone.
CREATE PROCEDURE dbo.Integration_Delete
    @Id          INT,
    @WorkspaceId INT
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

        SELECT WorkflowRefId, WorkflowName, NodeId, NodeName, IsLive
        FROM dbo.IntegrationUsages
        WHERE IntegrationId = @Id AND IsLive = 1
        ORDER BY WorkflowName, NodeName;
        RETURN;
    END

    DELETE FROM dbo.WorkflowIntegrationRefs WHERE IntegrationId = @Id;
    DELETE FROM dbo.Integrations WHERE Id = @Id AND WorkspaceId = @WorkspaceId;

    COMMIT TRAN;
END
GO
