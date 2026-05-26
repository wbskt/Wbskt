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
    ORDER BY Version DESC;
END;
GO
