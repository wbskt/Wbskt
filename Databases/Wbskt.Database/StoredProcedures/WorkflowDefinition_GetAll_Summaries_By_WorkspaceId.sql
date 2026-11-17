-- The workspace's workflows, one row each: the newest version's name and details, the workflow's
-- state and which version is live, and its draft when it has one. Deleted workflows are left out.
CREATE PROCEDURE dbo.WorkflowDefinition_GetAll_Summaries_By_WorkspaceId
    @WorkspaceId INT,
    @Skip INT,
    @Take INT,
    @TotalCount INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @TotalCount = COUNT(*)
    FROM dbo.Workflows
    WHERE WorkspaceId = @WorkspaceId
      AND State IN (1, 2, 3);

    WITH LatestWorkflows AS
    (
        SELECT
            WD.RefId,
            MAX(WD.Version) AS LatestVersion
        FROM dbo.Workflows W
        INNER JOIN dbo.WorkflowDefinitions WD ON WD.RefId = W.RefId AND WD.Kind = N'Version'
        WHERE W.WorkspaceId = @WorkspaceId
          AND W.State IN (1, 2, 3)
        GROUP BY WD.RefId
    )
    SELECT
        w.Id,
        w.RefId,
        w.Version,
        w.WorkspaceId,
        w.Name,
        w.Description,
        '{}' AS DefinitionJson,
        w.PublishedBy,
        w.CreatedAt,
        wf.State,
        live.Version AS LiveVersion,
        -- The trigger node kinds, distinct, in the order their first node appears, comma separated.
        -- Read from the definition rather than TriggerRegistrations, which a workflow that is not
        -- live has none of.
        (
            SELECT STRING_AGG(T.Kind, ',') WITHIN GROUP (ORDER BY T.FirstAt)
            FROM (
                SELECT JSON_VALUE(N.[value], '$.kind') AS Kind, MIN(CAST(N.[key] AS INT)) AS FirstAt
                FROM OPENJSON(w.DefinitionJson, '$.nodes') N
                WHERE JSON_VALUE(N.[value], '$.kind') LIKE 'trigger:%'
                GROUP BY JSON_VALUE(N.[value], '$.kind')
            ) T
        ) AS TriggerKinds,
        dr.Revision AS DraftRevision,
        dr.BaseVersion AS DraftBaseVersion,
        dr.UpdatedByRefId AS DraftUpdatedByRefId,
        dr.UpdatedAt AS DraftUpdatedAt
    FROM dbo.WorkflowDefinitions w
    INNER JOIN LatestWorkflows lw ON w.RefId = lw.RefId AND w.Version = lw.LatestVersion AND w.Kind = N'Version'
    INNER JOIN dbo.Workflows wf ON wf.RefId = w.RefId
    LEFT JOIN dbo.WorkflowDefinitions live ON live.Id = wf.LiveDefinitionId
    LEFT JOIN dbo.WorkflowDrafts dr ON dr.RefId = w.RefId
    ORDER BY w.Name ASC
    OFFSET @Skip ROWS
    FETCH NEXT @Take ROWS ONLY;
END;
GO
