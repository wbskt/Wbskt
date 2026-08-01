-- Direct permission grants on a user. These override role-derived permissions in both directions,
-- so being able to read them back is what makes an unexpected allow or deny diagnosable.
CREATE PROCEDURE dbo.UserPermission_GetAllForUser
    @UserId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        P.Slug,
        UP.IsDeny,
        W.RefId AS WorkspaceRefId
    FROM dbo.UserPermissions UP
    INNER JOIN dbo.Permissions P ON P.Id = UP.PermissionId
    LEFT JOIN dbo.Workspaces W ON W.Id = UP.WorkspaceId
    WHERE UP.UserId = @UserId
      AND UP.TenantId = @TenantId
    ORDER BY P.Slug;
END
GO
