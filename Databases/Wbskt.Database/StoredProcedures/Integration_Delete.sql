-- Deletes an integration unless a published workflow's current version sends through it. The row
-- is locked first, and WorkflowDefinition_Publish takes the same lock on every integration it
-- references, so a publish cannot add a use between the check and the delete.
--
-- Returns the blocking uses (published current versions) when it refused, and no rows when it
-- deleted (or there was nothing left to delete). Deprecated uses do not block: their refs go with the row, and
-- reinstating such a workflow is refused because the integration it names is gone.
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

    IF EXISTS (SELECT 1 FROM dbo.IntegrationUsages WHERE IntegrationId = @Id AND IsEnabled = 1)
    BEGIN
        COMMIT TRAN;

        SELECT WorkflowRefId, WorkflowName, NodeId, NodeName, IsEnabled
        FROM dbo.IntegrationUsages
        WHERE IntegrationId = @Id AND IsEnabled = 1
        ORDER BY WorkflowName, NodeName;
        RETURN;
    END

    DELETE FROM dbo.WorkflowIntegrationRefs WHERE IntegrationId = @Id;
    DELETE FROM dbo.Integrations WHERE Id = @Id AND WorkspaceId = @WorkspaceId;

    COMMIT TRAN;
END
GO
