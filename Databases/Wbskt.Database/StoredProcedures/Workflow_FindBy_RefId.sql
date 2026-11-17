-- A workflow's state and live version, scoped by workspace. No row for a workflow that is unknown,
-- another workspace's, deleted, or only a draft.
CREATE PROCEDURE dbo.Workflow_FindBy_RefId
    @RefId       UNIQUEIDENTIFIER,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        W.RefId,
        W.WorkspaceId,
        W.State,
        W.LiveDefinitionId,
        LD.Version AS LiveVersion,
        (SELECT MAX(V.Version) FROM dbo.WorkflowDefinitions V WHERE V.RefId = W.RefId AND V.Kind = N'Version') AS LatestVersion
      FROM dbo.Workflows W
      LEFT JOIN dbo.WorkflowDefinitions LD ON LD.Id = W.LiveDefinitionId
     WHERE W.RefId = @RefId
       AND W.WorkspaceId = @WorkspaceId
       AND W.State <> 4;
END;
GO
