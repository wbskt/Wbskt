-- The permissions a user effectively holds in a tenant, the one place the precedence rule is written:
-- for each permission, a user-level DENY beats everything, then a user-level ALLOW, then a role that
-- allows it provided no held role denies it; anything else is denied.
--
-- An assignment counts when it is in @TenantId and is tenant-wide (WorkspaceId NULL) or, when
-- @WorkspaceId is given, scoped to that workspace. With @WorkspaceId NULL only tenant-wide assignments
-- count, which is what a tenant-level check needs. Roles reached through a group and its ancestors count
-- when @ThroughGroups is 1; dbo.Tenant_Administrators passes 0 (see there for why).
--
-- A member suspended in the tenant holds nothing there, whatever is assigned to them.
CREATE FUNCTION dbo.User_EffectivePermissions (@UserId INT, @TenantId INT, @WorkspaceId INT, @ThroughGroups BIT)
RETURNS TABLE
AS
RETURN
    WITH EffectiveRoles AS (
        SELECT RoleId
        FROM dbo.User_EffectiveRoles(@UserId, @TenantId, @WorkspaceId, @ThroughGroups)
    ),
    UserPerm AS (
        SELECT PermissionId,
               MAX(CASE WHEN IsDeny = 1 THEN 1 ELSE 0 END) AS HasDeny,
               MAX(CASE WHEN IsDeny = 0 THEN 1 ELSE 0 END) AS HasAllow
        FROM dbo.UserPermissions
        WHERE UserId = @UserId
          AND TenantId = @TenantId
          AND (WorkspaceId IS NULL OR WorkspaceId = @WorkspaceId)
        GROUP BY PermissionId
    ),
    RolePerm AS (
        SELECT RP.PermissionId,
               MAX(CASE WHEN RP.IsDeny = 1 THEN 1 ELSE 0 END) AS HasDeny,
               MAX(CASE WHEN RP.IsDeny = 0 THEN 1 ELSE 0 END) AS HasAllow
        FROM dbo.RolePermissions RP
        INNER JOIN EffectiveRoles ER ON ER.RoleId = RP.RoleId
        GROUP BY RP.PermissionId
    )
    SELECT P.Id AS PermissionId,
           P.Slug
    FROM dbo.Permissions P
    LEFT JOIN UserPerm UP ON UP.PermissionId = P.Id
    LEFT JOIN RolePerm RP ON RP.PermissionId = P.Id
    WHERE NOT EXISTS (
              SELECT 1
              FROM dbo.TenantMembers TM
              WHERE TM.TenantId = @TenantId
                AND TM.UserId = @UserId
                AND TM.IsSuspended = 1)
      AND ISNULL(UP.HasDeny, 0) = 0
      AND (ISNULL(UP.HasAllow, 0) = 1 OR (ISNULL(RP.HasDeny, 0) = 0 AND ISNULL(RP.HasAllow, 0) = 1));
GO
