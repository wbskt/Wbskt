-- Every draft in a workspace, without its definition, newest edit first. Bounded by the draft ceiling
-- for new workflows plus one per published workflow, so it is not paged.
CREATE PROCEDURE dbo.WorkflowDraft_ListBy_WorkspaceId
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        D.RefId,
        D.WorkspaceId,
        D.BaseVersion,
        D.Revision,
        D.Name,
        D.Description,
        N'{}' AS DefinitionJson,
        D.CreatedBy,
        D.CreatedAt,
        D.UpdatedBy,
        D.UpdatedByRefId,
        D.UpdatedAt,
        (SELECT MAX(WD.Version) FROM dbo.WorkflowDefinitions WD WHERE WD.RefId = D.RefId AND WD.Kind = N'Version') AS CurrentVersion
      FROM dbo.WorkflowDrafts D
     WHERE D.WorkspaceId = @WorkspaceId
     ORDER BY D.UpdatedAt DESC;
END;
GO
