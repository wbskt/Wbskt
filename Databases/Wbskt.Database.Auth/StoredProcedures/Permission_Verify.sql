-- Verifies a single permission for a user within a scope.
-- @WorkspaceId NULL: only tenant-wide assignments count (used for tenant-level management checks).
-- @WorkspaceId set: tenant-wide + workspace-scoped assignments count.
-- Precedence: User-Deny > User-Allow > Role-Deny > Role-Allow > default DENY.
CREATE PROCEDURE dbo.Permission_Verify
    @UserId INT,
    @TenantId INT,
    @WorkspaceId INT = NULL,
    @PermissionSlug NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @PermissionId INT;

    -- 1. Get Permission ID
    SELECT @PermissionId = Id FROM dbo.Permissions WHERE Slug = @PermissionSlug;

    -- IF permission doesn't exist, strictly deny
    IF @PermissionId IS NULL
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- 2. Check Direct User Permission (Explicit Override)
    -- NOTE: when @WorkspaceId is NULL, "WorkspaceId = @WorkspaceId" is never true,
    -- so only tenant-wide rows (WorkspaceId IS NULL) match.
    DECLARE @UserDeny BIT, @UserAllow BIT;

    SELECT
        @UserDeny = MAX(CASE WHEN IsDeny = 1 THEN 1 ELSE 0 END),
        @UserAllow = MAX(CASE WHEN IsDeny = 0 THEN 1 ELSE 0 END)
    FROM dbo.UserPermissions
    WHERE UserId = @UserId
      AND PermissionId = @PermissionId
      AND TenantId = @TenantId
      AND (WorkspaceId IS NULL OR WorkspaceId = @WorkspaceId);

    -- IF User has explicit DENY -> DENY
    IF @UserDeny = 1
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- IF User has explicit ALLOW -> ALLOW
    IF @UserAllow = 1
    BEGIN
        SELECT 1 AS AccessGranted;
        RETURN;
    END

    -- 3. Resolve ALL Roles (Direct + GROUP Inherited) within scope
    ;WITH AllGroups AS (
        -- Anchor: Direct Groups within the tenant
        SELECT ug.GroupId
        FROM dbo.UserGroups ug
        INNER JOIN dbo.Groups g ON g.Id = ug.GroupId AND g.TenantId = @TenantId
        WHERE ug.UserId = @UserId

        UNION ALL

        -- Recursive: Parent Groups
        SELECT g.ParentGroupId
        FROM dbo.Groups g
        INNER JOIN AllGroups ag ON g.Id = ag.GroupId
        WHERE g.ParentGroupId IS NOT NULL
    )
    SELECT DISTINCT RoleId
    INTO #UserEffectiveRoles
    FROM (
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
    ) roles;

    -- 4. Check Role Permissions
    DECLARE @RoleDeny BIT = 0;
    DECLARE @RoleAllow BIT = 0;

    SELECT
        @RoleDeny = MAX(CASE WHEN IsDeny = 1 THEN 1 ELSE 0 END),
        @RoleAllow = MAX(CASE WHEN IsDeny = 0 THEN 1 ELSE 0 END)
    FROM dbo.RolePermissions rp
    INNER JOIN #UserEffectiveRoles uer ON uer.RoleId = rp.RoleId
    WHERE rp.PermissionId = @PermissionId;

    -- Clean up temp TABLE
    DROP TABLE #UserEffectiveRoles;

    -- Logic: Any Role DENY -> DENY
    IF @RoleDeny = 1
    BEGIN
        SELECT 0 AS AccessGranted;
        RETURN;
    END

    -- Logic: Any Role ALLOW (and no Deny) -> ALLOW
    IF @RoleAllow = 1
    BEGIN
        SELECT 1 AS AccessGranted;
        RETURN;
    END

    -- DEFAULT: DENY
    SELECT 0 AS AccessGranted;
END
GO
