CREATE PROCEDURE dbo.Workflow_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM dbo.Workflows
    WHERE Id = @Id;
END
