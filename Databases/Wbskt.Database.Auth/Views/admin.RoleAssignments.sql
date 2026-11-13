-- Who holds which role, tenant-wide (WorkspaceId NULL) or in one workspace.
CREATE VIEW [admin].RoleAssignments
AS
SELECT
    UR.UserId,
    UR.TenantId,
    UR.WorkspaceId,
    R.RefId AS RoleRefId,
    R.Name  AS RoleName,
    R.Kind  AS RoleKind
FROM dbo.UserRoles UR
INNER JOIN dbo.Roles R ON R.Id = UR.RoleId;
GO
