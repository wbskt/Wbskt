CREATE PROCEDURE dbo.Workflow_GetBy_Id
    @Id INT
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
    WHERE Id = @Id;
END
