CREATE PROCEDURE dbo.Workflow_GetAllBy_Workspace
    @WorkspaceId INT,
    @Skip INT = 0,
    @Take INT = 100,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Total count for the workspace
    SELECT @TotalCount = COUNT(*)
    FROM dbo.Workflows
    WHERE WorkspaceId = @WorkspaceId;

    -- Paginated selection
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
    ORDER BY CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
END
GO
