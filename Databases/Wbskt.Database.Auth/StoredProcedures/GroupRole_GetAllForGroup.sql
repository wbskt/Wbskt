CREATE PROCEDURE dbo.GroupRole_GetAllForGroup
    @GroupId INT,
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        R.RefId AS RoleRefId,
        R.Name AS RoleName,
        W.RefId AS WorkspaceRefId
    FROM dbo.GroupRoles GR
    INNER JOIN dbo.Roles R ON R.Id = GR.RoleId
    LEFT JOIN dbo.Workspaces W ON W.Id = GR.WorkspaceId
    WHERE GR.GroupId = @GroupId
      AND GR.TenantId = @TenantId
    ORDER BY R.Name;
END
GO
