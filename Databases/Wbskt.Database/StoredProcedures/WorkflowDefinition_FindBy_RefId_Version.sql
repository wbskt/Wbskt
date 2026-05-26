CREATE PROCEDURE dbo.WorkflowDefinition_FindBy_RefId_Version
    @RefId          UNIQUEIDENTIFIER,
    @Version        INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        Id
    FROM dbo.WorkflowDefinitions
    WHERE RefId = @RefId
      AND Version = @Version;
END;
GO
