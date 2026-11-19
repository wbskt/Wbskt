-- The workspace a definition belongs to, without its JSON: what a run's ownership check needs.
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
