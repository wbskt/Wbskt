CREATE PROCEDURE dbo.Workspace_FindBy_RefId
    @RefId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM dbo.Workspaces
    WHERE RefId = @RefId;
END
GO
