CREATE PROCEDURE dbo.WorkflowDefinition_GetAll_Summaries_By_WorkspaceId
    @WorkspaceId INT,
    @Skip INT,
    @Take INT,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Get total count for pagination
    SELECT @TotalCount = COUNT(DISTINCT RefId)
    FROM dbo.WorkflowDefinitions
    WHERE WorkspaceId = @WorkspaceId;

    -- Return latest versions of workflows in the workspace
    WITH LatestWorkflows AS
    (
        SELECT 
            RefId,
            MAX(Version) AS LatestVersion
        FROM dbo.WorkflowDefinitions
        WHERE WorkspaceId = @WorkspaceId
        GROUP BY RefId
    )
    SELECT
        w.Id,
        w.RefId,
        w.Version,
        w.WorkspaceId,
        w.Name,
        w.Description,
        w.IsEnabled,
        '{}' AS DefinitionJson,
        w.PublishedBy,
        w.CreatedAt
    FROM dbo.WorkflowDefinitions w
    INNER JOIN LatestWorkflows lw ON w.RefId = lw.RefId AND w.Version = lw.LatestVersion
    ORDER BY w.Name ASC
    OFFSET @Skip ROWS
    FETCH NEXT @Take ROWS ONLY;
END;
GO
