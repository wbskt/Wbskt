CREATE PROCEDURE dbo.WorkflowDefinition_GetLatestVersion_By_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        Id,
        RefId,
        Version,
        WorkspaceId,
        Name,
        Description,
        IsEnabled,
        DefinitionJson,
        PublishedBy,
        CreatedAt
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId
      -- A deleted workflow has no current version: reads, runs and publishes by RefId all stop here.
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WDel WHERE WDel.RefId = @RefId)
    ORDER BY Version DESC;
END;
GO
