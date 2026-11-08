-- A workflow's draft, with the workflow's current version so the caller can tell whether the draft
-- is behind it. Nothing when there is no draft in @WorkspaceId.
CREATE PROCEDURE dbo.WorkflowDraft_GetBy_RefId
    @RefId       UNIQUEIDENTIFIER,
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
        D.DefinitionJson,
        D.CreatedBy,
        D.CreatedAt,
        D.UpdatedBy,
        D.UpdatedByRefId,
        D.UpdatedAt,
        (SELECT MAX(WD.Version) FROM dbo.WorkflowDefinitions WD WHERE WD.RefId = D.RefId AND WD.Kind = N'Version') AS CurrentVersion
      FROM dbo.WorkflowDrafts D
     WHERE D.RefId = @RefId
       AND D.WorkspaceId = @WorkspaceId;
END;
GO
