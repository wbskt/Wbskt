-- The workspaces @UserId is a member of, leaving out every workspace of an account that suspended them.
-- Each row also says whether they hold the account's Administrator role (Roles.Kind = 'Admin') there,
-- account-wide or for that workspace, directly or through a group (dbo.User_EffectiveRoles), and how
-- many members it has: the workspaces list shows both on each card.
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
        W.TenantId,
        W.CreatedAt,
        T.RefId AS TenantRefId,
        CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.User_EffectiveRoles(@UserId, W.TenantId, W.Id, 1) ER
                INNER JOIN dbo.Roles R ON R.Id = ER.RoleId
                WHERE R.Kind = N'Admin')
             THEN 1 ELSE 0 END AS BIT) AS IsAdmin,
        (SELECT COUNT(*) FROM dbo.WorkspaceMembers M WHERE M.WorkspaceId = W.Id) AS MemberCount
    FROM dbo.Workspaces W
    INNER JOIN dbo.Tenants T ON T.Id = W.TenantId
    INNER JOIN dbo.WorkspaceMembers WM ON W.Id = WM.WorkspaceId
    WHERE WM.UserId = @UserId
      AND NOT EXISTS (
          SELECT 1 FROM dbo.TenantMembers TM
          WHERE TM.TenantId = W.TenantId AND TM.UserId = @UserId AND TM.IsSuspended = 1);
END
GO
