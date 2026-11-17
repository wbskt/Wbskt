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
        CAST(CASE WHEN W.LiveDefinitionId = WD.Id THEN 1 ELSE 0 END AS BIT) AS IsLive,
        WD.PublishedBy,
        WD.CreatedAt,
        (SELECT COUNT_BIG(*) FROM dbo.Runs RU WHERE RU.WorkflowDefinitionId = WD.Id) AS RunCount
    FROM dbo.WorkflowDefinitions WD
    INNER JOIN dbo.Workflows W ON W.RefId = WD.RefId AND W.State <> 4
    WHERE WD.RefId = @RefId
      AND WD.WorkspaceId = @WorkspaceId
      AND WD.Kind = N'Version'
    ORDER BY WD.Version DESC;
END
GO
