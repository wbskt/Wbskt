CREATE PROCEDURE dbo.Workspace_GetBy_Id
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, RefId, Name, Description, OwnerUserId, CreatedAt
    FROM dbo.Workspaces
    WHERE Id = @Id;
END
GO
