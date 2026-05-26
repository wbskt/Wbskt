CREATE PROCEDURE dbo.WorkflowDefinition_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        Id
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId
    ORDER BY Version DESC;
END;
GO
