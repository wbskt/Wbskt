-- Role assignments held by a user, with the scope each was granted at (NULL workspace = tenant-wide).
CREATE PROCEDURE dbo.UserRole_GetAllForUser
    @UserId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        R.RefId AS RoleRefId,
        R.Name AS RoleName,
        W.RefId AS WorkspaceRefId
    FROM dbo.UserRoles UR
    INNER JOIN dbo.Roles R ON R.Id = UR.RoleId
    LEFT JOIN dbo.Workspaces W ON W.Id = UR.WorkspaceId
    WHERE UR.UserId = @UserId
      AND UR.TenantId = @TenantId
    ORDER BY R.Name;
END
GO
