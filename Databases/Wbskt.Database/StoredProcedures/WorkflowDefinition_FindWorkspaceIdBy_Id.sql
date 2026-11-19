-- The workspace a definition belongs to, without its JSON: what a run's ownership check needed. Nothing
-- calls it since the run read by RefId carries the workspace; kept so the previous management image
-- keeps checking ownership while the migrator runs ahead of it, and dropped in the release after (#1059).
CREATE PROCEDURE dbo.WorkflowDefinition_FindWorkspaceIdBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WorkspaceId
    FROM dbo.WorkflowDefinitions
    WHERE Id = @Id;
END;
GO
