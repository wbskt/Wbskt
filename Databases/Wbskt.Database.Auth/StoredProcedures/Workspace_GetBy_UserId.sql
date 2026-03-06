CREATE PROCEDURE dbo.Workspace_GetBy_UserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        W.Id,
        W.RefId,
        W.Name,
        W.Description,
        W.OwnerUserId,
        W.CreatedAt
    FROM dbo.Workspaces W
    INNER JOIN dbo.WorkspaceMembers WM ON W.Id = WM.WorkspaceId
    WHERE WM.UserId = @UserId;
END
GO
