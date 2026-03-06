CREATE PROCEDURE dbo.Workflow_GetBy_RefId
    @RefId UNIQUEIDENTIFIER
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
    WHERE RefId = @RefId;
END
