-- Every published version of a workflow, newest first, without the definition JSON (fetch one with
-- WorkflowDefinition_GetBy_RefId_Version). Scoped by workspace; a deleted workflow lists nothing.
CREATE PROCEDURE dbo.WorkflowDefinition_GetVersions_By_RefId
    @RefId       UNIQUEIDENTIFIER,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WD.Version,
        WD.Name,
        WD.Description,
        WD.IsEnabled,
        WD.PublishedBy,
        WD.CreatedAt,
        (SELECT COUNT_BIG(*) FROM dbo.Runs RU WHERE RU.WorkflowDefinitionId = WD.Id) AS RunCount
    FROM dbo.WorkflowDefinitions WD
    WHERE WD.RefId = @RefId
      AND WD.WorkspaceId = @WorkspaceId
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WDel WHERE WDel.RefId = WD.RefId)
    ORDER BY WD.Version DESC;
END
GO
