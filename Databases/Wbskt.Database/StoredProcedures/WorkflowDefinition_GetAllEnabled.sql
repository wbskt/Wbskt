CREATE PROCEDURE dbo.WorkflowDefinition_GetAllEnabled
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        wd.Id,
        wd.RefId,
        wd.Version,
        wd.WorkspaceId,
        wd.Name,
        wd.Description,
        wd.IsEnabled,
        wd.DefinitionJson,
        wd.PublishedBy,
        wd.CreatedAt
    FROM dbo.WorkflowDefinitions wd
    INNER JOIN (
        SELECT RefId, MAX(Version) AS MaxVersion
        FROM dbo.WorkflowDefinitions
        WHERE WorkspaceId = @WorkspaceId
          AND IsEnabled = 1
        GROUP BY RefId
    ) latest ON wd.RefId = latest.RefId AND wd.Version = latest.MaxVersion
    WHERE wd.WorkspaceId = @WorkspaceId
      AND wd.IsEnabled = 1;
END;
GO
