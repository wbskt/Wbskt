CREATE PROCEDURE dbo.Workflow_GetAllEnabled -- ONLY TO BE USED BY WORKFLOW ENGINE
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
    WHERE IsEnabled = 1;
END
