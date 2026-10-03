-- Returns every permission slug the user effectively holds in the given workspace.
-- An assignment applies iff it belongs to the workspace's tenant AND is either
-- tenant-wide (WorkspaceId IS NULL) or scoped to this workspace.
-- Precedence per permission: User-Deny > User-Allow > Role-Deny > Role-Allow > default DENY.
CREATE PROCEDURE dbo.Permission_EffectiveSet
    @UserId INT,
    @WorkspaceId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Workspaces WHERE Id = @WorkspaceId);

    -- A member suspended in the workspace's tenant holds nothing there, whatever is assigned to them.
    IF @TenantId IS NULL
       OR EXISTS (SELECT 1 FROM dbo.TenantMembers WHERE TenantId = @TenantId AND UserId = @UserId AND IsSuspended = 1)
    BEGIN
        SELECT Slug FROM dbo.Permissions WHERE 1 = 0;
        RETURN;
    END

    ;WITH AllGroups AS (
        -- Anchor: user's direct groups within the tenant
        SELECT ug.GroupId
        FROM dbo.UserGroups ug
        INNER JOIN dbo.Groups g ON g.Id = ug.GroupId AND g.TenantId = @TenantId
        WHERE ug.UserId = @UserId

        UNION ALL

        -- Recursive: parent groups
        SELECT g.ParentGroupId
        FROM dbo.Groups g
        INNER JOIN AllGroups ag ON g.Id = ag.GroupId
        WHERE g.ParentGroupId IS NOT NULL
    ),
    EffectiveRoles AS (
        SELECT ur.RoleId
        FROM dbo.UserRoles ur
        WHERE ur.UserId = @UserId
          AND ur.TenantId = @TenantId
          AND (ur.WorkspaceId IS NULL OR ur.WorkspaceId = @WorkspaceId)

        UNION

        SELECT gr.RoleId
        FROM dbo.GroupRoles gr
        INNER JOIN AllGroups ag ON ag.GroupId = gr.GroupId
        WHERE gr.TenantId = @TenantId
          AND (gr.WorkspaceId IS NULL OR gr.WorkspaceId = @WorkspaceId)
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
        SELECT rp.PermissionId,
               MAX(CASE WHEN rp.IsDeny = 1 THEN 1 ELSE 0 END) AS HasDeny,
               MAX(CASE WHEN rp.IsDeny = 0 THEN 1 ELSE 0 END) AS HasAllow
        FROM dbo.RolePermissions rp
        INNER JOIN EffectiveRoles er ON er.RoleId = rp.RoleId
        GROUP BY rp.PermissionId
    )
    SELECT p.Slug
    FROM dbo.Permissions p
    LEFT JOIN UserPerm up ON up.PermissionId = p.Id
    LEFT JOIN RolePerm rp ON rp.PermissionId = p.Id
    WHERE ISNULL(up.HasDeny, 0) = 0
      AND (ISNULL(up.HasAllow, 0) = 1 OR (ISNULL(rp.HasDeny, 0) = 0 AND ISNULL(rp.HasAllow, 0) = 1));
END
GO
