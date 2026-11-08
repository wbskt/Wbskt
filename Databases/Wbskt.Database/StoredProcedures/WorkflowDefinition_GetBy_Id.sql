CREATE PROCEDURE dbo.WorkflowDefinition_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        RefId,
        Version,
        WorkspaceId,
        Name,
        Description,
        IsEnabled,
        DefinitionJson,
        PublishedBy,
        CreatedAt,
        Kind
    FROM dbo.WorkflowDefinitions
    WHERE Id = @Id;
END;
GO
