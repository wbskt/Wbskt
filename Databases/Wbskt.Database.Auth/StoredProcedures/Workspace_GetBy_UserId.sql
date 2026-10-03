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
    WHERE WM.UserId = @UserId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TenantMembers TM
          WHERE TM.TenantId = W.TenantId AND TM.UserId = @UserId AND TM.IsSuspended = 1);
END
GO
