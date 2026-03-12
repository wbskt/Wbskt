CREATE PROCEDURE dbo.Workflow_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Workflows
    WHERE RefId = @RefId;
END
GO
