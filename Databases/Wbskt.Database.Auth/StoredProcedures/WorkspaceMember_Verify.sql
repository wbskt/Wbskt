CREATE PROCEDURE dbo.WorkspaceMember_Verify
    @UserId INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.WorkspaceMembers WM
        INNER JOIN dbo.Workspaces W ON W.Id = WM.WorkspaceId
        WHERE WM.UserId = @UserId AND WM.WorkspaceId = @WorkspaceId
          -- Suspended in the workspace's tenant: the row stays, so lifting the suspension restores
          -- exactly what they had, but it does not count while it lasts.
          AND NOT EXISTS (
              SELECT 1 FROM dbo.TenantMembers TM
              WHERE TM.TenantId = W.TenantId AND TM.UserId = @UserId AND TM.IsSuspended = 1)
    ) THEN 1 ELSE 0 END AS BIT) AS IsMember;
END
GO
