-- Every definition a page of runs ran, in one call. Ids with no row are left out.
CREATE PROCEDURE dbo.WorkflowDefinition_GetBy_Ids
    @Ids dbo.IdTableType READONLY
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
        wd.DefinitionJson,
        wd.PublishedBy,
        wd.CreatedAt,
        wd.Kind
    FROM dbo.WorkflowDefinitions AS wd
    INNER JOIN @Ids AS ids ON ids.Id = wd.Id;
END;
GO
