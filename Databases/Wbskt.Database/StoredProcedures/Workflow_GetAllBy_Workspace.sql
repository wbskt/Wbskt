CREATE PROCEDURE dbo.Workflow_GetAllBy_Workspace
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        RefId,
        WorkspaceId,
        Name,
        Description,
        IsEnabled,
        Version,
        DefinitionJson,
        CreatedAt
    FROM dbo.Workflows
    WHERE WorkspaceId = @WorkspaceId
    ORDER BY CreatedAt DESC;
END
