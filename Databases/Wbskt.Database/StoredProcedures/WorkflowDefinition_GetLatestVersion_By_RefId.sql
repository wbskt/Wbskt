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
        DefinitionJson,
        PublishedBy,
        CreatedAt
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId
      AND Kind = N'Version'
      -- A deleted workflow has no current version: reads, runs and publishes by RefId all stop here.
      AND NOT EXISTS (SELECT 1 FROM dbo.Workflows W WHERE W.RefId = @RefId AND W.State = 4)
    ORDER BY Version DESC;
END;
GO
