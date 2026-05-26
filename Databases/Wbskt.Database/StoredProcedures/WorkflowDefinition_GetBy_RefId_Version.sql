CREATE PROCEDURE dbo.WorkflowDefinition_GetBy_RefId_Version
    @RefId    UNIQUEIDENTIFIER,
    @Version  INT
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
        CreatedAt
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId
      AND Version = @Version;
END;
GO
