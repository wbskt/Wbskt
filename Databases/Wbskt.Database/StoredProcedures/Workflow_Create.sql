CREATE PROCEDURE dbo.Workflow_Create
    @WorkspaceId INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @InsertedId TABLE (Id INT);

    INSERT INTO dbo.Workflows (WorkspaceId, Name, Description)
    OUTPUT INSERTED.Id INTO @InsertedId
    VALUES (@WorkspaceId, @Name, @Description);

    SELECT 
        w.Id,
        w.RefId,
        w.WorkspaceId,
        w.Name,
        w.Description,
        w.IsEnabled,
        w.Version,
        w.DefinitionJson,
        w.CreatedAt
    FROM dbo.Workflows w
    JOIN @InsertedId i ON w.Id = i.Id;
END
GO
