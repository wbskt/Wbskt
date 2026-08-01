CREATE PROCEDURE dbo.Workspace_Update
    @Id INT,
    @Name NVARCHAR(100),
    @Description NVARCHAR(500)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Workspaces
    SET Name = @Name,
        Description = @Description
    WHERE Id = @Id;
END
GO
