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
    FROM dbo.WorkflowDefinitions WD
    WHERE WorkspaceId = @WorkspaceId
      AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WDel WHERE WDel.RefId = WD.RefId);

    -- Return latest versions of workflows in the workspace
    WITH LatestWorkflows AS
    (
        SELECT 
            RefId,
            MAX(Version) AS LatestVersion
        FROM dbo.WorkflowDefinitions WD
        WHERE WorkspaceId = @WorkspaceId
          AND NOT EXISTS (SELECT 1 FROM dbo.WorkflowDeletions WDel WHERE WDel.RefId = WD.RefId)
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
        w.CreatedAt,
        -- The trigger node kinds, distinct, in the order their first node appears, comma separated.
        -- Read from the definition rather than TriggerRegistrations, which a deprecated version has none of.
        (
            SELECT STRING_AGG(T.Kind, ',') WITHIN GROUP (ORDER BY T.FirstAt)
            FROM (
                SELECT JSON_VALUE(N.[value], '$.kind') AS Kind, MIN(CAST(N.[key] AS INT)) AS FirstAt
                FROM OPENJSON(w.DefinitionJson, '$.nodes') N
                WHERE JSON_VALUE(N.[value], '$.kind') LIKE 'trigger:%'
                GROUP BY JSON_VALUE(N.[value], '$.kind')
            ) T
        ) AS TriggerKinds
    FROM dbo.WorkflowDefinitions w
    INNER JOIN LatestWorkflows lw ON w.RefId = lw.RefId AND w.Version = lw.LatestVersion
    ORDER BY w.Name ASC
    OFFSET @Skip ROWS
    FETCH NEXT @Take ROWS ONLY;
END;
GO
